using Microsoft.AspNetCore.Http;

namespace API.Application.Features.File.Commands;

public sealed record UploadFileCommand(int ChatId, int UserId, IFormFile File);
