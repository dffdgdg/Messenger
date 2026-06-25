using API.Application.Common;
using API.Application.Features.File.Commands;
using Shared.Contracts.Message;

namespace API.Application.Features.File;

public interface IFileHandlers
{
    ICommandHandler<UploadFileCommand, MessageFileDto> UploadFile { get; }
}