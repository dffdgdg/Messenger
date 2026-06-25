using Microsoft.AspNetCore.Http;

namespace API.Application.Features.Chat.Commands;

public sealed record UploadChatAvatarCommand(int ChatId, int UserId, IFormFile File);
