using MessengerAPI.Common;
using MessengerAPI.Model;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Message;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace MessengerAPI.Services.Messaging;

public interface ITranscriptionService
{
    Task<Result> TranscribeAsync(int messageId, CancellationToken ct = default);
    Task<Result<VoiceTranscriptionDTO>> GetTranscriptionAsync(int messageId, CancellationToken ct = default);
}

public class TranscriptionService(MessengerDbContext context,IHubNotifier hubNotifier,IWebHostEnvironment env,
    ILogger<TranscriptionService> logger) : ITranscriptionService
{
    public async Task<Result> TranscribeAsync(int messageId, CancellationToken ct = default)
    {
        var message = await context.Messages.Include(m => m.MessageFiles).FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (message == null)
            return Result.Failure($"Сообщение {messageId} не найдено");

        if (!message.IsVoiceMessage)
            return Result.Failure("Сообщение не является голосовым");

        var audioFile = message.MessageFiles?.FirstOrDefault(f => IsAudioFile(f.ContentType));

        if (audioFile == null)
            return Result.Failure("Аудиофайл не найден");

        try
        {
            // Статус → processing
            message.TranscriptionStatus = "processing";
            await context.SaveChangesAsync(ct);

            await hubNotifier.SendToChatAsync(message.ChatId,"TranscriptionStatusChanged",
                new VoiceTranscriptionDTO
                {
                    MessageId = messageId,
                    ChatId = message.ChatId,
                    Status = "processing"
                });

            var filePath = GetAbsolutePath(audioFile.Path);
            if (!File.Exists(filePath))
            {
                message.TranscriptionStatus = "failed";
                await context.SaveChangesAsync(ct);
                return Result.Failure("Аудиофайл не найден на диске");
            }

            // Распознавание
            var transcription = await RunWhisperAsync(filePath, ct);

            // Результат → Content
            message.Content = transcription;
            message.TranscriptionStatus =
                string.IsNullOrWhiteSpace(transcription)? "failed" : "completed";

            await context.SaveChangesAsync(ct);

            await hubNotifier.SendToChatAsync(message.ChatId,"TranscriptionCompleted",
                new VoiceTranscriptionDTO
                {
                    MessageId = messageId,
                    ChatId = message.ChatId,
                    Transcription = message.Content,
                    Status = message.TranscriptionStatus
                });

            logger.LogInformation("Транскрибация {MessageId}: {Status}", messageId, message.TranscriptionStatus);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Ошибка транскрибации {MessageId}", messageId);

            message.TranscriptionStatus = "failed";
            await context.SaveChangesAsync(CancellationToken.None);

            await hubNotifier.SendToChatAsync(message.ChatId,"TranscriptionStatusChanged",
                new VoiceTranscriptionDTO
                {
                    MessageId = messageId,
                    ChatId = message.ChatId,
                    Status = "failed"
                });

            return Result.Failure("Ошибка распознавания речи");
        }
    }

    public async Task<Result<VoiceTranscriptionDTO>> GetTranscriptionAsync(int messageId, CancellationToken ct = default)
    {
        var data = await context.Messages.AsNoTracking().Where(m => m.Id == messageId && m.IsVoiceMessage)
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

    #region Whisper

    private async Task<string?> RunWhisperAsync(string audioFilePath, CancellationToken ct)
    {
        var outputDir = Path.GetDirectoryName(audioFilePath)!;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "whisper",
                Arguments = $"\"{audioFilePath}\" " + "--model small --language ru " + "--output_format txt " + $"--output_dir \"{outputDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            logger.LogWarning("Whisper ошибка: {Error}", error);
            return null;
        }

        var txtPath = Path.ChangeExtension(audioFilePath, ".txt");
        if (!File.Exists(txtPath))
            return null;

        var text = await File.ReadAllTextAsync(txtPath, ct);

        try { File.Delete(txtPath); }
        catch {  }

        return text?.Trim();
    }

    #endregion

    #region Helpers

    private string GetAbsolutePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;

        return Path.Combine(env.WebRootPath ?? "wwwroot",relativePath.TrimStart('/'));
    }

    private static bool IsAudioFile(string? contentType)
        => contentType?.StartsWith("audio/",StringComparison.OrdinalIgnoreCase) == true;

    #endregion
}