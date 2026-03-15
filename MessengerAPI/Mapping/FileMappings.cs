namespace MessengerAPI.Mapping;

public static class FileMappings
{
    public static MessageFileDto ToDto(this MessageFile file, IUrlBuilder? urlBuilder = null) => new()
    {
        Id = file.Id,
        MessageId = file.MessageId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        Url = file.Path.BuildFullUrl(urlBuilder),
        PreviewType = DeterminePreviewType(file.ContentType),
        FileSize = GetFileSize(file.Path)
    };

    private static long GetFileSize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return 0;

        var normalized = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var candidatePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", normalized),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", normalized)
        };

        return candidatePaths.Where(File.Exists).Select(p => new FileInfo(p).Length)
            .FirstOrDefault();
    }

    public static string DeterminePreviewType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return "file";

        var type = contentType.ToLowerInvariant();

        return type switch
        {
            _ when type.StartsWith("image/") => "image",
            _ when type.StartsWith("video/") => "video",
            _ when type.StartsWith("audio/") => "audio",
            _ => "file"
        };
    }
}