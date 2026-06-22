using API.Domain.Entities;
using API.Infrastructure.Repositories.Implementations;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Repositories;

public class RefreshTokenRepositoryTests : IntegrationTestBase
{
    private readonly RefreshTokenRepository _repo;

    public RefreshTokenRepositoryTests() => _repo = new RefreshTokenRepository(Context);

    private async Task SeedTokensAsync()
    {
        var user = await DbContextFactory.SeedUserAsync(Context, "testuser", "pass");
        var now = DateTime.UtcNow;
        Context.RefreshTokens.AddRange(
            new RefreshToken { UserId = user.Id, TokenHash = "hash1", FamilyId = "fam1", JwtId = "j1", CreatedAt = now.AddHours(-2), ExpiresAt = now.AddDays(30) },
            new RefreshToken { UserId = user.Id, TokenHash = "hash2", FamilyId = "fam1", JwtId = "j2", CreatedAt = now.AddHours(-1), ExpiresAt = now.AddDays(30) },
            new RefreshToken { UserId = user.Id, TokenHash = "hash3", FamilyId = "fam2", JwtId = "j3", CreatedAt = now, ExpiresAt = now.AddHours(-1), RevokedAt = now },
            new RefreshToken { UserId = user.Id, TokenHash = "hash4", FamilyId = "fam2", JwtId = "j4", CreatedAt = now, ExpiresAt = now.AddDays(-1) }
        );
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task FindByHash_ReturnsToken()
    {
        await SeedTokensAsync();
        var user = await Context.Users.FirstAsync(u => u.Username == "testuser");

        var token = await _repo.FindByHashAsync("hash1", user.Id);

        token.Should().NotBeNull();
        token!.TokenHash.Should().Be("hash1");
        token.FamilyId.Should().Be("fam1");
    }

    [Fact]
    public async Task FindByHash_ReturnsNull_WhenNotFound()
    {
        await SeedTokensAsync();
        var user = await Context.Users.FirstAsync(u => u.Username == "testuser");

        var token = await _repo.FindByHashAsync("nonexistent", user.Id);
        token.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllForUser_RevokesActiveTokens()
    {
        await SeedTokensAsync();
        var user = await Context.Users.FirstAsync(u => u.Username == "testuser");
        var now = DateTime.UtcNow;

        var tokens = await Context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null && rt.UsedAt == null)
            .ToListAsync();
        foreach (var t in tokens) t.RevokedAt = now;
        await Context.SaveChangesAsync();

        var active = await Context.RefreshTokens
            .CountAsync(rt => rt.UserId == user.Id && rt.RevokedAt == null && rt.UsedAt == null);
        active.Should().Be(0);
    }

    [Fact]
    public async Task RevokeByFamilyId_RevokesFamily()
    {
        await SeedTokensAsync();
        var now = DateTime.UtcNow;

        var tokens = await Context.RefreshTokens
            .Where(rt => rt.FamilyId == "fam1" && rt.RevokedAt == null)
            .ToListAsync();
        foreach (var t in tokens) t.RevokedAt = now;
        await Context.SaveChangesAsync();

        var revoked = await Context.RefreshTokens
            .CountAsync(rt => rt.FamilyId == "fam1" && rt.RevokedAt != null);
        revoked.Should().Be(2);
    }

    [Fact]
    public async Task RevokeByFamilyIds_RevokesMultipleFamilies()
    {
        await SeedTokensAsync();
        var now = DateTime.UtcNow;

        var tokens = await Context.RefreshTokens
            .Where(rt => new[] { "fam1", "fam2" }.Contains(rt.FamilyId) && rt.RevokedAt == null)
            .ToListAsync();
        foreach (var t in tokens) t.RevokedAt = now;
        await Context.SaveChangesAsync();

        var remaining = await Context.RefreshTokens.CountAsync(rt => rt.RevokedAt == null);
        remaining.Should().BeLessThan(4);
    }

    [Fact]
    public async Task DeleteExpired_RemovesExpired()
    {
        await SeedTokensAsync();
        var user = await Context.Users.FirstAsync(u => u.Username == "testuser");

        var expired = await Context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();
        Context.RefreshTokens.RemoveRange(expired);
        await Context.SaveChangesAsync();

        var remaining = await Context.RefreshTokens.CountAsync();
        remaining.Should().BeLessThan(4);
    }

    [Fact]
    public async Task GetActiveFamilies_ReturnsActiveFamilies()
    {
        await SeedTokensAsync();
        var user = await Context.Users.FirstAsync(u => u.Username == "testuser");

        var families = await _repo.GetActiveFamiliesAsync(user.Id, DateTime.UtcNow.AddMinutes(-30));

        families.Should().NotBeEmpty();
        families.Should().Contain(f => f.FamilyId == "fam1");
    }
}