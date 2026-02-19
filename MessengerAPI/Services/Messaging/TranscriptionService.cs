using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Message;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using System.Diagnostics;

namespace MessengerAPI.Services.Messaging;

public interface ITranscriptionService
{
    Task<Result> TranscribeAsync(int messageId, CancellationToken ct = default);
    Task<Result<VoiceTranscriptionDTO>> GetTranscriptionAsync(int messageId, CancellationToken ct = default);
}

public class TranscriptionService : ITranscriptionService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TranscriptionService> _logger;

    private readonly string _whisperPath;
    private readonly string _modelPath;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    private const int TargetSampleRate = 16000;
    private const int TargetBitsPerSample = 16;
    private const int TargetChannels = 1;

    public TranscriptionService(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment env,
        ILogger<TranscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _logger = logger;

        _whisperPath = Path.Combine(
            _env.ContentRootPath,
            "whisper",
            OperatingSystem.IsWindows() ? "whisper.exe" : "whisper");

        _modelPath = Path.Combine(_env.ContentRootPath,"whisper","models","ggml-small-q5_1.bin");

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

        var message = await context.Messages
            .Include(m => m.MessageFiles)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (message == null)
            return Result.Failure($"Сообщение {messageId} не найдено");

        if (!message.IsVoiceMessage)
            return Result.Failure("Сообщение не является голосовым");

        var audioFile = message.MessageFiles?
            .FirstOrDefault(f => IsAudioFile(f.ContentType));

        if (audioFile == null)
            return Result.Failure("Аудиофайл не найден");

        try
        {
            await SetStatusAsync(context, hubNotifier, message, "processing", ct);

            var filePath = GetAbsolutePath(audioFile.Path);
            if (!File.Exists(filePath))
            {
                await SetFailedAsync(context, hubNotifier, message);
                return Result.Failure("Аудиофайл не найден на диске");
            }

            var transcription = await RecognizeAsync(filePath, ct);

            message.Content = transcription;
            message.TranscriptionStatus =
                string.IsNullOrWhiteSpace(transcription) ? "failed" : "done";

            await context.SaveChangesAsync(ct);

            await hubNotifier.SendToChatAsync(
                message.ChatId, "TranscriptionCompleted",
                new VoiceTranscriptionDTO
                {
                    MessageId = messageId,
                    ChatId = message.ChatId,
                    Transcription = message.Content,
                    Status = message.TranscriptionStatus
                });

            _logger.LogInformation(
                "Транскрибация {Id}: {Status}",
                messageId, message.TranscriptionStatus);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Транскрибация {Id} отменена", messageId);
            await SetFailedAsync(context, hubNotifier, message);
            return Result.Failure("Транскрибация отменена");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка транскрибации {Id}", messageId);
            await SetFailedAsync(context, hubNotifier, message);
            return Result.Failure("Ошибка распознавания речи");
        }
    }

    public async Task<Result<VoiceTranscriptionDTO>> GetTranscriptionAsync(
        int messageId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();

        var data = await context.Messages
            .AsNoTracking()
            .Where(m => m.Id == messageId && m.IsVoiceMessage)
            .Select(m => new VoiceTranscriptionDTO
            {
                MessageId = m.Id,
                ChatId = m.ChatId,
                Transcription = m.Content,
                Status = m.TranscriptionStatus ?? "pending"
            })
            .FirstOrDefaultAsync(ct);

        if (data == null)
            return Result<VoiceTranscriptionDTO>.Failure("Голосовое сообщение не найдено");

        return Result<VoiceTranscriptionDTO>.Success(data);
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

    private async Task<string?> ProcessAudioWithWhisper(string audioFilePath, CancellationToken ct)
    {
        var pcmData = ConvertToPcm16Mono16K(audioFilePath, ct);
        if (pcmData.Length == 0)
            return null;

        var tempWav = Path.GetTempFileName() + ".wav";

        await using (var writer = new WaveFileWriter(
            tempWav,
            new WaveFormat(TargetSampleRate, TargetBitsPerSample, TargetChannels)))
        {
            writer.Write(pcmData, 0, pcmData.Length);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _whisperPath,
            Arguments = $"-m \"{_modelPath}\" -f \"{tempWav}\" -l ru -nt -of -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return null;

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        try { File.Delete(tempWav); } catch { }

        var text = output?.Trim();
        _logger.LogDebug("Whisper: {Text}", text);

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    #endregion

    #region Audio Conversion

    private static byte[] ConvertToPcm16Mono16K(string audioFilePath, CancellationToken ct)
    {
        using var reader = new AudioFileReader(audioFilePath);

        var targetFormat = new WaveFormat(
            TargetSampleRate,
            TargetBitsPerSample,
            TargetChannels);

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
        MessengerDbContext context,
        IHubNotifier hubNotifier,
        Message message,
        string status,
        CancellationToken ct)
    {
        message.TranscriptionStatus = status;
        await context.SaveChangesAsync(ct);

        await hubNotifier.SendToChatAsync(
            message.ChatId,
            "TranscriptionStatusChanged",
            new VoiceTranscriptionDTO
            {
                MessageId = message.Id,
                ChatId = message.ChatId,
                Status = status
            });
    }

    private static async Task SetFailedAsync(
        MessengerDbContext context,
        IHubNotifier hubNotifier,
        Message message)
    {
        message.TranscriptionStatus = "failed";
        await context.SaveChangesAsync(CancellationToken.None);

        await hubNotifier.SendToChatAsync(
            message.ChatId,
            "TranscriptionStatusChanged",
            new VoiceTranscriptionDTO
            {
                MessageId = message.Id,
                ChatId = message.ChatId,
                Status = "failed"
            });
    }

    private string GetAbsolutePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;

        return Path.Combine(
            _env.WebRootPath ?? "wwwroot",
            relativePath.TrimStart('/'));
    }

    private static bool IsAudioFile(string? contentType)
        => contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
