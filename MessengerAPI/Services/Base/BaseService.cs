namespace MessengerAPI.Services.Base;

public abstract class BaseService<T>(MessengerDbContext context, ILogger<T> logger) where T : class
{
    protected readonly MessengerDbContext _context = context;
    protected readonly ILogger<T> _logger = logger;

    protected async Task<Result> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Конфликт конкурентного обновления");
            return Result.Conflict("Данные были изменены другим пользователем. Попробуйте снова.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(ex, "Нарушение уникальности при сохранении");
            return Result.Conflict("Запись с такими данными уже существует");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Ошибка обновления базы данных");
            return Result.Internal("Не удалось сохранить изменения");
        }
    }

    protected async Task<Result<TEntity>> FindEntityAsync<TEntity>(int id, CancellationToken ct = default)
        where TEntity : class
    {
        var entity = await _context.FindAsync<TEntity>([id], ct);
        return entity is not null
            ? Result<TEntity>.Success(entity)
            : Result<TEntity>.NotFound($"{typeof(TEntity).Name} с ID {id} не найден");
    }

    protected static IQueryable<TEntity> Paginate<TEntity>(IQueryable<TEntity> query, int page, int pageSize)
        => query.Skip((page - 1) * pageSize).Take(pageSize);

    protected static (int Page, int PageSize) NormalizePagination(int page, int pageSize, int maxSize = 100)
        => (Page: Math.Max(1, page), PageSize: Math.Clamp(pageSize, 1, maxSize));

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? "";
        return message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505");
    }
}