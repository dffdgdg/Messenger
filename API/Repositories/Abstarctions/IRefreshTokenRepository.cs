using API.Repositories.Base;

namespace API.Repositories.Abstarctions;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, int userId, CancellationToken ct = default);
    Task<int> RevokeAllForUserAsync(int userId, DateTime revokedAt, CancellationToken ct = default);
    Task<int> RevokeByFamilyIdAsync(string familyId, DateTime revokedAt, CancellationToken ct = default);
    Task<int> RevokeByFamilyIdsAsync(IEnumerable<string> familyIds, DateTime revokedAt, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(int userId, DateTime olderThan, CancellationToken ct = default);
    Task<List<ActiveFamily>> GetActiveFamiliesAsync(int userId, DateTime now, CancellationToken ct = default);
}

public sealed record ActiveFamily(string FamilyId, DateTime LatestCreatedAt);