using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Message;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using Vosk;
using System.Text.Json;

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
    private readonly Vosk.Model _model;
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

        var modelPath = FindModelPath();
        Vosk.Vosk.SetLogLevel(-1);
        _model = new Vosk.Model(modelPath);

        _logger.LogInformation("Vosk инициализирован: {Path}", modelPath);
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
        return await Task.Run(() =>
        {
            _semaphore.Wait(ct);
            try
            {
                return ProcessAudio(audioFilePath, ct);
            }
            finally
            {
                _semaphore.Release();
            }
        }, ct);
    }

    private string? ProcessAudio(string audioFilePath, CancellationToken ct)
    {
        var pcmData = ConvertToPcm16Mono16K(audioFilePath, ct);

        if (pcmData.Length == 0)
        {
            _logger.LogWarning("Пустой аудиофайл: {Path}", audioFilePath);
            return null;
        }

        using var recognizer = new VoskRecognizer(_model, TargetSampleRate);
        recognizer.SetMaxAlternatives(0);
        recognizer.SetWords(false);

        const int chunkSize = 4096;
        var chunk = new byte[chunkSize];

        for (var offset = 0; offset < pcmData.Length; offset += chunkSize)
        {
            ct.ThrowIfCancellationRequested();

            var count = Math.Min(chunkSize, pcmData.Length - offset);
            Array.Copy(pcmData, offset, chunk, 0, count);
            recognizer.AcceptWaveform(chunk, count);
        }

        var result = recognizer.FinalResult();
        var text = ExtractText(result);

        _logger.LogDebug("Vosk: '{Text}' ({Bytes} bytes PCM)", text, pcmData.Length);

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    #endregion

    #region Audio Conversion

    private byte[] ConvertToPcm16Mono16K(string audioFilePath, CancellationToken ct)
    {
        using var reader = new WaveFileReader(audioFilePath);
        var sourceFormat = reader.WaveFormat;

        if (IsTargetFormat(sourceFormat))
            return ReadAllBytes(reader, ct);

        _logger.LogDebug(
            "Конвертация: {Rate}Hz/{Bits}bit/{Ch}ch → 16000Hz/16bit/1ch",
            sourceFormat.SampleRate, sourceFormat.BitsPerSample, sourceFormat.Channels);

        var floatSamples = ReadAsFloatSamples(reader, ct);
        var monoSamples = ConvertToMono(floatSamples, sourceFormat.Channels);
        var resampledSamples = Resample(monoSamples, sourceFormat.SampleRate, TargetSampleRate);

        return ConvertFloatToPcm16(resampledSamples);
    }

    private static bool IsTargetFormat(WaveFormat format)
    {
        return format.SampleRate == TargetSampleRate
            && format.BitsPerSample == TargetBitsPerSample
            && format.Channels == TargetChannels
            && format.Encoding == WaveFormatEncoding.Pcm;
    }

    private static byte[] ReadAllBytes(WaveFileReader reader, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            ms.Write(buffer, 0, bytesRead);
        }

        return ms.ToArray();
    }

    private static float[] ReadAsFloatSamples(WaveFileReader reader, CancellationToken ct)
    {
        var sampleProvider = reader.ToSampleProvider();
        var samples = new List<float>();
        var buffer = new float[8192];
        int samplesRead;

        while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            for (var i = 0; i < samplesRead; i++)
                samples.Add(buffer[i]);
        }

        return samples.ToArray();
    }

    private static float[] ConvertToMono(float[] samples, int channels)
    {
        if (channels == 1) return samples;

        var monoLength = samples.Length / channels;
        var mono = new float[monoLength];

        for (var i = 0; i < monoLength; i++)
        {
            var sum = 0f;
            for (var ch = 0; ch < channels; ch++)
                sum += samples[i * channels + ch];
            mono[i] = sum / channels;
        }

        return mono;
    }

    private static float[] Resample(float[] samples, int sourceSampleRate, int targetSampleRate)
    {
        if (sourceSampleRate == targetSampleRate) return samples;

        var ratio = (double)sourceSampleRate / targetSampleRate;
        var newLength = (int)(samples.Length / ratio);
        var resampled = new float[newLength];

        for (var i = 0; i < newLength; i++)
        {
            var srcIndex = i * ratio;
            var index = (int)srcIndex;
            var fraction = (float)(srcIndex - index);

            if (index + 1 < samples.Length)
                resampled[i] = samples[index] * (1 - fraction) + samples[index + 1] * fraction;
            else if (index < samples.Length)
                resampled[i] = samples[index];
        }

        return resampled;
    }

    private static byte[] ConvertFloatToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            var value = (short)(clamped * short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }

    #endregion

    #region JSON

    private static string? ExtractText(string jsonResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResult);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
                return textElement.GetString();
        }
        catch { }

        return null;
    }

    #endregion

    #region Helpers

    private static async Task SetStatusAsync(
        MessengerDbContext context, IHubNotifier hubNotifier,
        Message message, string status, CancellationToken ct)
    {
        message.TranscriptionStatus = status;
        await context.SaveChangesAsync(ct);

        await hubNotifier.SendToChatAsync(
            message.ChatId, "TranscriptionStatusChanged",
            new VoiceTranscriptionDTO
            {
                MessageId = message.Id,
                ChatId = message.ChatId,
                Status = status
            });
    }

    private static async Task SetFailedAsync(
        MessengerDbContext context, IHubNotifier hubNotifier, Message message)
    {
        message.TranscriptionStatus = "failed";
        await context.SaveChangesAsync(CancellationToken.None);

        await hubNotifier.SendToChatAsync(
            message.ChatId, "TranscriptionStatusChanged",
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

    private string FindModelPath()
    {
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Models"),
            Path.Combine(Directory.GetCurrentDirectory(), "Models"),
            Path.Combine(_env.ContentRootPath, "Models"),
            Path.Combine(_env.ContentRootPath, "Model")
        };

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            var modelDir = Directory.GetDirectories(basePath, "vosk-model*")
                .FirstOrDefault();

            if (modelDir != null) return modelDir;
        }

        throw new FileNotFoundException(
            "Vosk модель не найдена. Скачайте:\n" +
            "https://alphacephei.com/vosk/models → vosk-model-small-ru-0.22.zip\n" +
            "Распакуйте в папку Models/");
    }

    private static bool IsAudioFile(string? contentType)
        => contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model.Dispose();
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}