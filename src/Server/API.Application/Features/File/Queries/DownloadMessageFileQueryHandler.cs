using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;

namespace API.Application.Features.File.Queries;

public class DownloadMessageFileQueryHandler(IFileService fileService)
    : IQueryHandler<DownloadFileQuery, Result<FileDownloadInfo>>
{
    public async Task<Result<FileDownloadInfo>> HandleAsync(DownloadFileQuery query, CancellationToken ct = default)
        => await fileService.ResolveDownloadAsync(query.FileId, query.ContextMessageId, query.UserId, ct);
}