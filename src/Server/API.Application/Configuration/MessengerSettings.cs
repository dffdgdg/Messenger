namespace API.Application.Configuration;

public sealed class MessengerSettings
{
    public const string SectionName = "Messenger";
    public int AdminDepartmentId { get; set; } = 1;
    public const int MaxFileSizeMegabytes = 300;
    public long MaxFileSizeBytes { get; set; } = MaxFileSizeMegabytes * 1024L * 1024L;
    public int BcryptWorkFactor { get; set; } = 12;
    public int MaxImageDimension { get; set; } = 100;
    public int ImageQuality { get; set; } = 85;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 100;
}
