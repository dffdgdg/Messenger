using API.Repositories.Base;
using API.Repositories.Projections;

namespace API.Repositories.Abstarctions;

public interface IUserRepository : IRepository<User>
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> FindByIdWithPasswordAsync(int id, CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);
    Task<List<UserWithSettingsProjection>> GetAllWithSettingsAsync( CancellationToken ct = default);
    Task<UserWithSettingsProjection?> GetWithSettingsAsync(int id, CancellationToken ct = default);
    Task<bool> UsernameExistsByOtherUserAsync(string username, int excludeUserId, CancellationToken ct = default);
    Task<List<User>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
}