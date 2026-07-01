namespace API.Application.Features.File.Queries;

public sealed record DownloadFileQuery(int FileId, int? ContextMessageId, int UserId);
public sealed record DownloadVoiceQuery(int MessageId, int UserId);
public sealed record DownloadUserAvatarQuery(int TargetUserId, int ViewerId);
public sealed record DownloadChatAvatarQuery(int ChatId, int ViewerId);