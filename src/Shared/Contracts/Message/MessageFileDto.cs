namespace Shared.Contracts.Message;

public class MessageFileDto
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string PreViewType { get; set; } = "file";
    public long FileSize { get; set; }

    /// <summary>
    /// Одноразовый токен, полученный при загрузке файла на сервер.
    /// Используется только при создании сообщения (CreateMessageRequest.Files),
    /// после чего сервер удаляет его. Для уже созданных сообщений всегда null.
    /// </summary>
    public string? UploadToken { get; set; }
}