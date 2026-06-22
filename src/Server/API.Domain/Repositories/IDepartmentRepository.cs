using API.Domain.Entities;
using Shared.Enum;

namespace API.Domain.Repositories;

public interface IDepartmentRepository
{
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
    Task<Department?> FindByIdAsync(int id, CancellationToken ct = default);
    Task<Department?> FindByIdWithHeadAsync(int id, CancellationToken ct = default);
    Task<List<Department>> GetAllWithHeadAsync(CancellationToken ct = default);
    Task<List<Department>> GetAllAsync(CancellationToken ct = default);
    Task<bool> HasChildDepartmentsAsync(int id, CancellationToken ct = default);
    Task<bool> HasUsersAsync(int id, CancellationToken ct = default);
    Task<bool> IsHeadOfAnyDepartmentAsync(int userId, CancellationToken ct = default);
    Task<bool> IsHeadOfDepartmentAsync(int userId, int departmentId, CancellationToken ct = default);
    Task<int> CountUsersAsync(int departmentId, CancellationToken ct = default);
    Task<UserRole> ResolveUserRoleAsync(int userId, int adminDepartmentId, CancellationToken ct = default);
    Task<Dictionary<int, int>> GetUserCountsAsync(CancellationToken ct = default);
    Task<SystemSetting?> GetSystemSettingAsync(string key, CancellationToken ct = default);
    Task<Dictionary<int, DepartmentChatInfo>> GetChatIdsForDepartmentsAsync(IEnumerable<int> departmentIds, CancellationToken ct = default);
    void Add(Department department);
    void Remove(Department department);
}

public sealed record DepartmentChatInfo(int DepartmentId, int ChatId, string ChatName, ChatType ChatType, int? CreatedById, string? Avatar, bool ShowHistoryForNewMembers);