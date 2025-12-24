using MessengerAPI.Model;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public abstract class BaseService<T>(MessengerDbContext context, ILogger<T> logger) where T : class
    {
        protected readonly MessengerDbContext _context = context;
        protected readonly ILogger<T> _logger = logger;

        protected async Task<bool> SaveChangesAsync(CancellationToken ct = default)
        {
            try
            {
                return await _context.SaveChangesAsync(ct) > 0;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Ошибка обновления базы данных");
                throw new InvalidOperationException("Не удалось сохранить изменения", ex);
            }
        }

        protected async Task<TEntity> GetRequiredEntityAsync<TEntity>(int id,CancellationToken ct = default) where TEntity : class
        {
            var entity = await _context.FindAsync<TEntity>([id], ct);

            return entity is null ? throw new KeyNotFoundException($"{typeof(TEntity).Name} с ID {id} не найден") : entity;
        }

        protected static void EnsureNotNull<TEntity>(TEntity? entity, int id) where TEntity : class
        {
            if (entity is null)
            {
                throw new KeyNotFoundException($"{typeof(TEntity).Name} с ID {id} не найден");
            }
        }

        protected IQueryable<TEntity> Paginate<TEntity>(IQueryable<TEntity> query, int page, int pageSize) 
            => query.Skip((page - 1) * pageSize).Take(pageSize);

        protected (int Page, int PageSize) NormalizePagination(int page, int pageSize, int defaultSize = 50, int maxSize = 100) => (
                Page: Math.Max(1, page),
                PageSize: Math.Clamp(pageSize, 1, maxSize)
            );
    }
}