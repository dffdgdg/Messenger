namespace API.Application.Services.Abstractions;

public sealed record PendingUpload(
    string Token,
    int UserId,
    int ChatId,
    string AbsolutePath,
    string RelativePath,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime ExpiresAtUtc);

/// <summary>
/// Хранит информацию о файлах, уже сохранённых на диск, но ещё не привязанных
/// к сообщению. Токен одноразовый: используется ровно один раз при создании
/// сообщения (CreateMessageCommandHandler), после чего удаляется.
/// Записи, которые никто не забрал (пользователь передумал отправлять), 
/// удаляются фоновым сервисом по истечении TTL.
/// </summary>
public interface IPendingUploadStore
{
    string Register(int userId, int chatId, string absolutePath, string relativePath, string fileName, string contentType, long fileSize);
    bool TryConsume(string token, int userId, int chatId, out PendingUpload? upload);
    IReadOnlyCollection<PendingUpload> GetExpired(DateTime utcNow);
    void Remove(string token);
}