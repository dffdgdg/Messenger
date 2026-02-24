namespace MessengerShared.Dto.Message;

public class VoiceTranscriptionDto
{
    public int MessageId { get; set; }
    public int ChatId { get; set; }
    public string? Transcription { get; set; }
    public string Status { get; set; } = "pending";
}