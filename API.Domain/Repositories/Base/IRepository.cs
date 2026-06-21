namespace API.Domain.Repositories.Base;

public interface IRepository<TEntity> where TEntity : class
{
    Task<TEntity?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
    void Add(TEntity entity);
    void Remove(TEntity entity);
}