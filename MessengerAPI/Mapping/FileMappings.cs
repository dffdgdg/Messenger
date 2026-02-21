using MessengerAPI.Model;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.Message;

namespace MessengerAPI.Mapping;

public static class FileMappings
{
    public static MessageFileDTO ToDto(this MessageFile file, IUrlBuilder? urlBuilder = null) => new()
        {
            Id = file.Id,
            MessageId = file.MessageId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Url = file.Path.BuildFullUrl(urlBuilder),
            PreviewType = DeterminePreviewType(file.ContentType)
        };

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