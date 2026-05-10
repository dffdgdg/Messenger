using API.Repositories.Abstarctions;
using API.Repositories.Base;

namespace API.Repositories.Implementations;

public sealed class RefreshTokenRepository(MessengerDbContext context) : RepositoryBase<RefreshToken>(context), IRefreshTokenRepository
{
    public async Task<RefreshToken?> FindByHashAsync(string tokenHash, int userId, CancellationToken ct = default)
        => await _context.RefreshTokens.Include(rt => rt.User).FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.UserId == userId, ct);

    public async Task<List<RefreshToken>> GetActiveByUserIdAsync(int userId, CancellationToken ct = default)
        => await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null && rt.ExpiresAt > DateTime.UtcNow)
                                       .ToListAsync(ct);

    public async Task<int> RevokeAllForUserAsync(int userId, DateTime revokedAt, CancellationToken ct = default)
        => await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null)
                                       .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, revokedAt), ct);

    public async Task<int> RevokeByFamilyIdAsync(string familyId, DateTime revokedAt, CancellationToken ct = default)
        => await _context.RefreshTokens.Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, revokedAt), ct);

    public async Task<int> DeleteExpiredAsync(int userId, DateTime olderThan, CancellationToken ct = default)
        => await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.ExpiresAt < olderThan).ExecuteDeleteAsync(ct);

    public async Task<List<ActiveFamily>> GetActiveFamiliesAsync(int userId, DateTime now, CancellationToken ct = default)
        => await _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.UsedAt == null && rt.ExpiresAt > now)
        .GroupBy(rt => rt.FamilyId).Select(g => new { FamilyId = g.Key, LatestCreatedAt = g.Max(rt => rt.CreatedAt) }).OrderByDescending(g => g.LatestCreatedAt)
        .Select(g => new ActiveFamily(g.FamilyId, g.LatestCreatedAt)).ToListAsync(ct);

    public async Task<int> RevokeByFamilyIdsAsync(IEnumerable<string> familyIds, DateTime revokedAt, CancellationToken ct = default)
    {
        var ids = familyIds.ToList();
        return await _context.RefreshTokens.Where(rt => ids.Contains(rt.FamilyId) && rt.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, revokedAt), ct);
    }
}