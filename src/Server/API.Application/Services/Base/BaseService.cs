using API.Application.Services.Abstractions;
using API.Domain.Common;
using Microsoft.Extensions.Logging;

namespace API.Application.Services.Base;

public abstract class BaseService<T>(IUnitOfWork unitOfWork, ILogger<T> logger) where T : class
{
    protected readonly IUnitOfWork _unitOfWork = unitOfWork;
    protected readonly ILogger<T> _logger = logger;

    protected Task<Result> SaveChangesAsync(CancellationToken ct = default)
        => _unitOfWork.SaveChangesAsync(ct);

    protected static IQueryable<TEntity> Paginate<TEntity>(
        IQueryable<TEntity> query, int page, int pageSize)
        => query.Skip((page - 1) * pageSize).Take(pageSize);

    protected static (int Page, int PageSize) NormalizePagination(int page, int pageSize, int maxPageSize = 100)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, maxPageSize);
        return (normalizedPage, normalizedPageSize);
    }
}
