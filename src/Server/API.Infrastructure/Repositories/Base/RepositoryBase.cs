using API.Domain.Repositories.Base;
using API.Infrastructure.Database;

namespace API.Infrastructure.Repositories.Base;

public abstract class RepositoryBase<TEntity>(MessengerDbContext context)
    : IRepository<TEntity> where TEntity : class
{
    protected readonly MessengerDbContext _context = context;

    public async Task<TEntity?> FindByIdAsync(int id, CancellationToken ct = default)
        => await _context.FindAsync<TEntity>([id], ct);

    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
        => await _context.FindAsync<TEntity>([id], ct) is not null;

    public void Add(TEntity entity)
        => _context.Set<TEntity>().Add(entity);

    public void Remove(TEntity entity)
        => _context.Set<TEntity>().Remove(entity);
}