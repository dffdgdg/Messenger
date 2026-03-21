using MessengerShared.Dto.Message;

namespace MessengerShared.DTO.Message;

public class CreateMessageRequest
{
    [Required]
    public int ChatId { get; set; }

    [MaxLength(4000)]
    public string? Content { get; set; }

    public int? ReplyToMessageId { get; set; }
    public int? ForwardedFromMessageId { get; set; }

    // ── Voice ──
    public bool IsVoiceMessage { get; set; }
    public string? VoiceFileUrl { get; set; }
    public string? VoiceFileName { get; set; }
    public string? VoiceContentType { get; set; }
    public long? VoiceFileSize { get; set; }
    public double? VoiceDurationSeconds { get; set; }

    // ── Files ──
    public List<MessageFileDto>? Files { get; set; }
}