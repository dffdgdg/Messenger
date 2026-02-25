namespace MessengerAPI.Configuration;

public class MessengerSettings
{
    public const string SectionName = "Messenger";
    public int AdminDepartmentId { get; set; } = 1;
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public int BcryptWorkFactor { get; set; } = 12;
    public int MaxImageDimension { get; set; } = 1600;
    public int ImageQuality { get; set; } = 85;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 100;
}