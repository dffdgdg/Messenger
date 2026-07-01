using API.Application.Common;
using API.Application.Features.File.Commands;
using API.Application.Features.File.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using Shared.Contracts.Message;

namespace API.Application.Features.File;

public interface IFileHandlers
{
    ICommandHandler<UploadFileCommand, MessageFileDto> UploadFile { get; }
    IQueryHandler<DownloadFileQuery, Result<FileDownloadInfo>> DownloadFile { get; }
    IQueryHandler<DownloadVoiceQuery, Result<FileDownloadInfo>> DownloadVoice { get; }
    IQueryHandler<DownloadUserAvatarQuery, Result<FileDownloadInfo>> DownloadUserAvatar { get; }
    IQueryHandler<DownloadChatAvatarQuery, Result<FileDownloadInfo>> DownloadChatAvatar { get; }
}