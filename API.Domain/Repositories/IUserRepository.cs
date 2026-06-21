using API.Domain.Entities;
using API.Domain.Projections;
using API.Domain.Repositories.Base;

namespace API.Domain.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> FindByIdWithPasswordAsync(int id, CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);
    Task<List<UserWithSettingsProjection>> GetAllWithSettingsAsync( CancellationToken ct = default);
    Task<UserWithSettingsProjection?> GetWithSettingsAsync(int id, CancellationToken ct = default);
    Task<List<User>> GetExpiredStatusUsersAsync(DateTime now, CancellationToken ct = default);
    Task<bool> UsernameExistsByOtherUserAsync(string username, int excludeUserId, CancellationToken ct = default);
    Task<List<User>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task<User?> FindByIdTrackedAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsByUsernameExcludingAsync(string username, int excludeUserId, CancellationToken ct = default);
    Task<List<int>> GetUserIdsByDepartmentAsync(int departmentId, CancellationToken ct = default);
    Task<User?> FindByIdWithSettingsTrackedAsync(int id, CancellationToken ct = default);
}