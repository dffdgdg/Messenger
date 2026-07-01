using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;

namespace API.Application.Features.File.Queries;

public class DownloadChatAvatarQueryHandler(IFileService fileService)
    : IQueryHandler<DownloadChatAvatarQuery, Result<FileDownloadInfo>>
{
    public async Task<Result<FileDownloadInfo>> HandleAsync(DownloadChatAvatarQuery query, CancellationToken ct = default)
        => await fileService.ResolveChatAvatarDownloadAsync(query.ChatId, query.ViewerId, ct);
}