namespace MessengerAPI.Model;

public class VoiceMessage
{
    public int MessageId { get; set; }

    public double DurationSeconds { get; set; }

    public string TranscriptionStatus { get; set; } = "pending";

    public string? TranscriptionText { get; set; }

    public string FilePath { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = "audio/wav";

    public long FileSize { get; set; }

    public virtual Message Message { get; set; } = null!;
}