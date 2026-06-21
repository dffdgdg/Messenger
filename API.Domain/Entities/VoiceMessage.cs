namespace API.Domain.Entities;

public class VoiceMessage
{
    public int MessageId { get; set; }
    public double DurationSeconds { get; set; }
    public string? Waveform { get; set; }
    public string FilePath { get; set; } = null!;
    public long FileSize { get; set; }
    public virtual UserMessage Message { get; set; } = null!;
}