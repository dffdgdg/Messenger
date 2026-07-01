using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;

namespace API.Application.Features.File.Queries;

public class DownloadUserAvatarQueryHandler(IFileService fileService)
    : IQueryHandler<DownloadUserAvatarQuery, Result<FileDownloadInfo>>
{
    public async Task<Result<FileDownloadInfo>> HandleAsync(DownloadUserAvatarQuery query, CancellationToken ct = default)
        => await fileService.ResolveUserAvatarDownloadAsync(query.TargetUserId, query.ViewerId, ct);
}