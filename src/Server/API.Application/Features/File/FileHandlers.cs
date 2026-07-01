using API.Application.Features.File.Commands;
using API.Application.Features.File.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using Shared.Contracts.Message;

namespace API.Application.Features.File;

public class FileHandlers(
    UploadFileCommandHandler uploadFile,
    DownloadMessageFileQueryHandler downloadFile,
    DownloadVoiceQueryHandler downloadVoice,
    DownloadUserAvatarQueryHandler downloadUserAvatar,
    DownloadChatAvatarQueryHandler downloadChatAvatar) : IFileHandlers
{
    public ICommandHandler<UploadFileCommand, MessageFileDto> UploadFile => uploadFile;
    public IQueryHandler<DownloadFileQuery, Result<FileDownloadInfo>> DownloadFile => downloadFile;
    public IQueryHandler<DownloadVoiceQuery, Result<FileDownloadInfo>> DownloadVoice => downloadVoice;
    public IQueryHandler<DownloadUserAvatarQuery, Result<FileDownloadInfo>> DownloadUserAvatar => downloadUserAvatar;
    public IQueryHandler<DownloadChatAvatarQuery, Result<FileDownloadInfo>> DownloadChatAvatar => downloadChatAvatar;
}