using API.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace API.Application.Services.Abstractions;

public interface IUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}