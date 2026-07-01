namespace Shared.Contracts.Message;

public class CreateMessageRequest
{
    [Required]
    public int ChatId { get; set; }
    [MaxLength(4000)]
    public string? Content { get; set; }
    public int? ReplyToMessageId { get; set; }
    public int? ForwardedFromMessageId { get; set; }
    public bool IsVoiceMessage { get; set; }

    /// <summary>
    /// Одноразовый токен, полученный при загрузке голосового через тот же
    /// upload-эндпоинт, что и обычные файлы. Путь/размер файла сервер
    /// достаёт из PendingUpload — клиенту доверять эти поля нельзя.
    /// </summary>
    public string? VoiceUploadToken { get; set; }
    public double? VoiceDurationSeconds { get; set; }
    public string? VoiceWaveform { get; set; }
    public List<MessageFileDto>? Files { get; set; }
}