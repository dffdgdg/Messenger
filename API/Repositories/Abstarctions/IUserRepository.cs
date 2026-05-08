using API.Repositories.Base;

namespace API.Repositories.Abstarctions;

public interface IUserRepository : IRepository<User>
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> FindByIdWithPasswordAsync(int id, CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
    Task<List<User>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
}