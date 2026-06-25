using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using Shared.Contracts.Message;

namespace API.Application.Features.File.Commands;

public class UploadFileCommandHandler(IFileService fileService)
    : ICommandHandler<UploadFileCommand, MessageFileDto>
{
    public virtual async Task<Result<MessageFileDto>> HandleAsync(UploadFileCommand command, CancellationToken ct = default)
        => await fileService.SaveMessageFileAsync(command.File, command.ChatId, command.UserId);
}

