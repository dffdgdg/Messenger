using API.Domain.Entities;
using API.Domain.Projections;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace API.Infrastructure.Repositories.Implementations;

public sealed class UserRepository(MessengerDbContext context) : RepositoryBase<User>(context), IUserRepository
{
    public async Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => await _context.Users.Include(u => u.Password).AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);
    public Task<User?> FindByIdTrackedAsync(int id, CancellationToken ct = default)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    public Task<bool> ExistsByUsernameExcludingAsync(string username, int excludeUserId, CancellationToken ct = default)
        => _context.Users.AnyAsync(u => u.Username == username && u.Id != excludeUserId, ct);
    public Task<User?> FindByIdWithSettingsTrackedAsync(int id, CancellationToken ct = default)
        => _context.Users.Include(u => u.UserSetting).FirstOrDefaultAsync(u => u.Id == id, ct);
    public async Task<User?> FindByIdWithPasswordAsync(int id, CancellationToken ct = default)
        => await _context.Users.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => await _context.Users.AnyAsync(u => u.Username == username.Trim(), ct);
    public Task<List<User>> GetExpiredStatusUsersAsync(DateTime now, CancellationToken ct = default)
        => _context.Users.Where(u => u.StatusExpiresAt != null && u.StatusExpiresAt <= now).ToListAsync(ct);
    public async Task<bool> UsernameExistsByOtherUserAsync(string username, int excludeUserId, CancellationToken ct = default)
        => await _context.Users.AnyAsync(u => u.Username == username && u.Id != excludeUserId, ct);
    public async Task<List<UserWithSettingsProjection>> GetAllWithSettingsAsync(CancellationToken ct = default)
        => await ProjectWithSettings(_context.Users.OrderBy(u => u.Surname).ThenBy(u => u.Name)).ToListAsync(ct);

    public async Task<UserWithSettingsProjection?> GetWithSettingsAsync(int id, CancellationToken ct = default)
        => await ProjectWithSettings(_context.Users.Where(u => u.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<List<User>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
        => await _context.Users.Where(u => ids.Contains(u.Id)).AsNoTracking().ToListAsync(ct);

    public Task<List<int>> GetUserIdsByDepartmentAsync(int departmentId, CancellationToken ct = default)
        => _context.Users.Where(u => u.DepartmentId == departmentId).Select(u => u.Id).ToListAsync(ct);
    private static IQueryable<UserWithSettingsProjection> ProjectWithSettings(IQueryable<User> query)
        => query.Select(u => new UserWithSettingsProjection(u.Id, u.Username, u.Surname, u.Name, u.Midname,
            u.Avatar, u.DepartmentId, u.Department != null ? u.Department.Name : null, u.IsBanned, u.LastOnline,
            u.CreatedAt,u.UserSetting != null ? u.UserSetting.Theme : null, u.UserSetting == null || u.UserSetting.NotificationsEnabled,
            u.StatusType, u.StatusExpiresAt)).AsNoTracking();
}