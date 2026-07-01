using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;

namespace API.Application.Features.File.Queries;

public class DownloadVoiceQueryHandler(IFileService fileService)
    : IQueryHandler<DownloadVoiceQuery, Result<FileDownloadInfo>>
{
    public async Task<Result<FileDownloadInfo>> HandleAsync(DownloadVoiceQuery query, CancellationToken ct = default)
        => await fileService.ResolveVoiceDownloadAsync(query.MessageId, query.UserId, ct);
}