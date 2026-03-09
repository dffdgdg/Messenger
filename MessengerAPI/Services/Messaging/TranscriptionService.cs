using NAudio.Wave;
using System.Diagnostics;

namespace MessengerAPI.Services.Messaging;

public interface ITranscriptionService
{
    Task<Result> TranscribeAsync(int messageId, CancellationToken ct = default);
    Task<Result<VoiceTranscriptionDto>> GetTranscriptionAsync(int messageId, CancellationToken ct = default);
    Task<Result> RetryTranscriptionAsync(int messageId, CancellationToken ct = default);
}

public sealed class TranscriptionService : ITranscriptionService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscriptionQueue _transcriptionQueue;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TranscriptionService> _logger;

    private readonly string _whisperPath;
    private readonly string _modelPath;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const int TargetSampleRate = 16000;
    private const int TargetBitsPerSample = 16;
    private const int TargetChannels = 1;

    public TranscriptionService(IServiceScopeFactory scopeFactory, TranscriptionQueue transcriptionQueue,
        IWebHostEnvironment env, ILogger<TranscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _transcriptionQueue = transcriptionQueue;
        _env = env;
        _logger = logger;

        _whisperPath = Path.Combine(_env.ContentRootPath, "whisper",
            OperatingSystem.IsWindows() ? "whisper.exe" : "whisper");

        _modelPath = Path.Combine(
            _env.ContentRootPath, "whisper", "models", "ggml-small-q5_1.bin");

        if (!File.Exists(_whisperPath))
            throw new FileNotFoundException("whisper бинарник не найден");

        if (!File.Exists(_modelPath))
            throw new FileNotFoundException("whisper модель не найдена");

        _logger.LogInformation("Whisper инициализирован: {Model}", _modelPath);
    }

    #region Public API

    public async Task<Result> TranscribeAsync(int messageId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
        var hubNotifier = scope.ServiceProvider.GetRequiredService<IHubNotifier>();

        var voiceMessage = await context.VoiceMessages.Include(v => v.Message)
            .FirstOrDefaultAsync(v => v.MessageId == messageId, ct);

        if (voiceMessage is null)
            return Result.Failure($"Голосовое сообщение {messageId} не найдено");

        try
        {
            await SetStatusAsync(context, hubNotifier, voiceMessage, "processing", ct);

            var filePath = GetAbsolutePath(voiceMessage.FilePath);
            if (!File.Exists(filePath))
            {
                await SetFailedSafeAsync(context, hubNotifier, voiceMessage);
                return Result.Failure("Аудиофайл не найден на диске");
            }

            var transcription = await RecognizeAsync(filePath, ct);

            voiceMessage.TranscriptionText = transcription;
            voiceMessage.TranscriptionStatus = string.IsNullOrWhiteSpace(transcription) ? "failed" : "done";

            await context.SaveChangesAsync(ct);

            await hubNotifier.SendToChatAsync(voiceMessage.Message.ChatId, "TranscriptionCompleted",
                new VoiceTranscriptionDto
                {
                    MessageId = messageId,
                    ChatId = voiceMessage.Message.ChatId,
                    Transcription = voiceMessage.TranscriptionText,
                    Status = voiceMessage.TranscriptionStatus
                });

            _logger.LogInformation("Транскрибация {Id}: {Status}", messageId, voiceMessage.TranscriptionStatus);

            return Result.Success();
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Транскрибация {Id} отменена", messageId);
            await SetFailedSafeAsync(context, hubNotifier, voiceMessage);
            return Result.Failure("Транскрибация отменена");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка транскрибации {Id}", messageId);
            await SetFailedSafeAsync(context, hubNotifier, voiceMessage);
            return Result.Failure("Ошибка распознавания речи");
        }
    }

    public async Task<Result<VoiceTranscriptionDto>> GetTranscriptionAsync(int messageId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

        var data = await context.VoiceMessages.AsNoTracking()
            .Where(v => v.MessageId == messageId)
            .Select(v => new VoiceTranscriptionDto
            {
                MessageId = v.MessageId,
                ChatId = v.Message.ChatId,
                Transcription = v.TranscriptionText,
                Status = v.TranscriptionStatus
            })
            .FirstOrDefaultAsync(ct);

        if (data is null)
            return Result<VoiceTranscriptionDto>.Failure("Голосовое сообщение не найдено");

        return Result<VoiceTranscriptionDto>.Success(data);
    }

    public async Task<Result> RetryTranscriptionAsync(
        int messageId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

        var voiceMessage = await context.VoiceMessages.AsNoTracking().FirstOrDefaultAsync(v => v.MessageId == messageId, ct);

        if (voiceMessage is null)
            return Result.Failure("Голосовое сообщение не найдено");

        if (voiceMessage.TranscriptionStatus == "processing")
            return Result.Failure("Расшифровка уже выполняется");

        await _transcriptionQueue.EnqueueAsync(messageId, ct);
        return Result.Success();
    }

    #endregion

    #region Recognition

    private async Task<string?> RecognizeAsync(string audioFilePath, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            return await ProcessAudioWithWhisper(audioFilePath, ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string?> ProcessAudioWithWhisper(
        string audioFilePath, CancellationToken ct)
    {
        var pcmData = ConvertToPcm16Mono16K(audioFilePath, ct);
        if (pcmData.Length == 0)
            return null;

        var tempWav = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.wav");

        await using (var writer = new WaveFileWriter(
            tempWav, new WaveFormat(TargetSampleRate, TargetBitsPerSample, TargetChannels)))
        {
            await writer.WriteAsync(pcmData, ct);
        }

        var psi = new ProcessStartInfo(_whisperPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(_modelPath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(tempWav);
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add("ru");
        psi.ArgumentList.Add("-nt");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("-");

        Process? process = null;

        try
        {
            process = Process.Start(psi);
            if (process is null)
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
                _logger.LogWarning("Whisper stderr: {Error}", error);

            var text = output?.Trim();
            _logger.LogDebug("Whisper: {Text}", text);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException)
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        _logger.LogWarning("Whisper process killed due to timeout/cancellation");
                    }
                }
                catch (Exception killEx)
                {
                    _logger.LogError(killEx, "Failed to kill whisper process");
                }
            }

            throw;
        }
        finally
        {
            process?.Dispose();
            try { File.Delete(tempWav); } catch { /* cleanup best-effort */ }
        }
    }

    #endregion

    #region Audio Conversion

    private static byte[] ConvertToPcm16Mono16K(string audioFilePath, CancellationToken ct)
    {
        using var reader = new AudioFileReader(audioFilePath);

        var targetFormat = new WaveFormat(
            TargetSampleRate, TargetBitsPerSample, TargetChannels);

        using var resampler = new MediaFoundationResampler(reader, targetFormat)
        {
            ResamplerQuality = 60
        };

        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            ms.Write(buffer, 0, bytesRead);
        }

        return ms.ToArray();
    }

    #endregion

    #region Helpers

    private static async Task SetStatusAsync(
        MessengerDbContext context, IHubNotifier hubNotifier,
        VoiceMessage voiceMessage, string status, CancellationToken ct)
    {
        voiceMessage.TranscriptionStatus = status;
        await context.SaveChangesAsync(ct);

        await hubNotifier.SendToChatAsync(voiceMessage.Message.ChatId, "TranscriptionStatusChanged",
            new VoiceTranscriptionDto
            {
                MessageId = voiceMessage.MessageId,
                ChatId = voiceMessage.Message.ChatId,
                Status = status
            });
    }

    private async Task SetFailedSafeAsync(
        MessengerDbContext context, IHubNotifier hubNotifier, VoiceMessage voiceMessage)
    {
        try
        {
            voiceMessage.TranscriptionStatus = "failed";
            await context.SaveChangesAsync(CancellationToken.None);

            await hubNotifier.SendToChatAsync(voiceMessage.Message.ChatId, "TranscriptionStatusChanged",
                new VoiceTranscriptionDto
                {
                    MessageId = voiceMessage.MessageId,
                    ChatId = voiceMessage.Message.ChatId,
                    Status = "failed"
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"Не удалось установить статус failed для сообщения {MessageId}", voiceMessage.MessageId);
        }
    }

    private string GetAbsolutePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;

        return Path.Combine(_env.WebRootPath ?? "wwwroot", relativePath.TrimStart('/'));
    }

    #endregion

    public void Dispose() => _semaphore.Dispose();
}