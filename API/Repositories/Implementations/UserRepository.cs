using API.Repositories.Abstarctions;
using API.Repositories.Base;

namespace API.Repositories.Implementations;

public sealed class UserRepository(MessengerDbContext context) : RepositoryBase<User>(context), IUserRepository
{
    public async Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => await _context.Users.Include(u => u.Password).AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);

    public async Task<User?> FindByIdWithPasswordAsync(int id, CancellationToken ct = default)
        => await _context.Users.Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => await _context.Users.AnyAsync(u => u.Username == username.Trim(), ct);

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
        => await _context.Users.AsNoTracking().ToListAsync(ct);

    public async Task<List<User>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
        => await _context.Users.Where(u => ids.Contains(u.Id)).AsNoTracking().ToListAsync(ct);
}