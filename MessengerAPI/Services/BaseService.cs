using MessengerAPI.Model;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public abstract class BaseService<T>(MessengerDbContext context, ILogger<T> logger) where T : class
    {
        protected readonly MessengerDbContext _context = context;
        protected readonly ILogger<T> _logger = logger;

        protected async Task<bool> SaveChangesAsync()
        {
            try
            {
                return await _context.SaveChangesAsync() > 0;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Ошибка обновления базы данных");
                throw new Exception("Не удалось сохранить изменения в базу данных", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка сохранения изменений");
                throw;
            }
        }

        protected async Task<TEntity> FindEntityAsync<TEntity>(params object[] keyValues) where TEntity : class
        {
            TEntity entity = await _context.FindAsync<TEntity>(keyValues) ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} не найден");
            return entity;
        }

        protected async Task LoadCollectionAsync<TEntity, TProperty>(TEntity entity, string navigationProperty) where TEntity : class where TProperty : class => 
            await _context.Entry(entity).Collection(navigationProperty).LoadAsync();

        protected async Task LoadReferenceAsync<TEntity, TProperty>(TEntity entity, string navigationProperty) where TEntity : class where TProperty : class => 
            await _context.Entry(entity).Reference(navigationProperty).LoadAsync();

        protected void LogOperationError(Exception ex, string operation)
        {
            _logger.LogError(ex, "Ошибка при выполнении операции: {Operation}", operation);
            throw new Exception($"Ошибка при выполнении операции '{operation}': {ex.Message}", ex);
        }

        protected void LogOperationError(Exception ex, string operation, int id)
        {
            _logger.LogError(ex, "Ошибка при выполнении операции {Operation} для ID {Id}", operation, id);
            throw new Exception($"Ошибка при выполнении операции '{operation}' для ID {id}: {ex.Message}", ex);
        }

        protected void ValidateEntityExists<TEntity>(TEntity? entity, string entityName, object id) where TEntity : class
        {
            if (entity == null)
                throw new KeyNotFoundException($"{entityName} с ID {id} не найден");
        }

        protected IQueryable<TEntity> ApplyPagination<TEntity>(IQueryable<TEntity> query, int page, int pageSize) =>  query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}