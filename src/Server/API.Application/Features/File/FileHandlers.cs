using API.Application.Common;
using API.Application.Features.File.Commands;
using API.Domain.Common;
using Shared.Contracts.Message;

namespace API.Application.Features.File;

public class FileHandlers(UploadFileCommandHandler uploadFile) : IFileHandlers
{
    public ICommandHandler<UploadFileCommand, MessageFileDto> UploadFile => uploadFile;
}