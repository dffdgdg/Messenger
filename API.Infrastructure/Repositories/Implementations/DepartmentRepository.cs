using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Shared.Enum;

namespace API.Infrastructure.Repositories.Implementations;

public sealed class DepartmentRepository(MessengerDbContext context) : IDepartmentRepository
{
    public Task<bool> ExistsAsync(int id, CancellationToken ct = default)
        => context.Departments.AnyAsync(d => d.Id == id, ct);

    public Task<Department?> FindByIdAsync(int id, CancellationToken ct = default)
        => context.Departments.FindAsync([id], ct).AsTask();

    public Task<Department?> FindByIdWithHeadAsync(int id, CancellationToken ct = default)
        => context.Departments.Include(d => d.Head).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<List<Department>> GetAllWithHeadAsync(CancellationToken ct = default)
        => context.Departments.Include(d => d.Head).AsNoTracking().ToListAsync(ct);

    public Task<List<Department>> GetAllAsync(CancellationToken ct = default)
        => context.Departments.AsNoTracking().ToListAsync(ct);

    public Task<bool> HasChildDepartmentsAsync(int id, CancellationToken ct = default)
        => context.Departments.AnyAsync(d => d.ParentDepartmentId == id, ct);

    public Task<bool> HasUsersAsync(int id, CancellationToken ct = default)
        => context.Users.AnyAsync(u => u.DepartmentId == id, ct);

    public Task<bool> IsHeadOfAnyDepartmentAsync(int userId, CancellationToken ct = default)
        => context.Departments.AnyAsync(d => d.HeadId == userId, ct);

    public Task<bool> IsHeadOfDepartmentAsync(int userId, int departmentId, CancellationToken ct = default)
        => context.Departments.AnyAsync(d => d.Id == departmentId && d.HeadId == userId, ct);

    public Task<int> CountUsersAsync(int departmentId, CancellationToken ct = default)
        => context.Users.CountAsync(u => u.DepartmentId == departmentId, ct);

    public async Task<UserRole> ResolveUserRoleAsync(int userId, int adminDepartmentId, CancellationToken ct = default)
    {
        var role = UserRole.User;

        var isAdmin = await context.Users.AnyAsync(u => u.Id == userId && u.DepartmentId == adminDepartmentId, ct);
        if (isAdmin)
            role |= UserRole.Admin;

        var isHead = await context.Departments.AnyAsync(d => d.HeadId == userId, ct);
        if (isHead)
            role |= UserRole.Head;

        return role;
    }

    public async Task<Dictionary<int, int>> GetUserCountsAsync(CancellationToken ct = default)
        => await context.Users.Where(u => u.DepartmentId != null)
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

    public Task<SystemSetting?> GetSystemSettingAsync(string key, CancellationToken ct = default)
        => context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task<Dictionary<int, DepartmentChatInfo>> GetChatIdsForDepartmentsAsync(IEnumerable<int> departmentIds, CancellationToken ct = default)
    {
        var ids = departmentIds.ToList();
        if (ids.Count == 0) return [];

        return await context.Departments
            .Where(d => ids.Contains(d.Id) && d.ChatId.HasValue)
            .Select(d => new DepartmentChatInfo(
                d.Id,
                d.ChatId!.Value,
                d.Chat != null ? d.Chat.Name : $"Отдел {d.Name}",
                d.Chat != null ? d.Chat.Type : ChatType.Department,
                d.Chat!.CreatedById,
                d.Chat!.Avatar,
                d.Chat == null || d.Chat.ShowHistoryForNewMembers
            ))
            .ToDictionaryAsync(x => x.DepartmentId, x => x, ct);
    }

    public void Add(Department department)
        => context.Departments.Add(department);

    public void Remove(Department department)
        => context.Departments.Remove(department);
}