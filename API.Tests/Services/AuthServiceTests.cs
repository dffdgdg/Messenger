using API.Common;
using API.Configuration;
using API.Data;
using API.Services.Auth;
using API.Services.Infrastructure.Bundles;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class AuthServiceTests
{
    private static (AuthService Service, MessengerDbContext Db) CreateService(
        int adminDepartmentId = 99)
    {
        var db = DbContextFactory.Create();

        var jwtOptions = Options.Create(new JwtSettings
        {
            Secret = "super-secret-key-for-testing-32chars!!",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 30,
            Issuer = "API",
            Audience = "MessengerClient"
        });

        var messengerOptions = Options.Create(new MessengerSettings
        {
            AdminDepartmentId = adminDepartmentId,
            BcryptWorkFactor = 4
        });

        var tokenServiceMock = new Mock<ITokenService>();
        tokenServiceMock
            .Setup(t => t.GenerateTokenPair(It.IsAny<int>(), It.IsAny<UserRole?>()))
            .Returns(new TokenPair
            {
                AccessToken = "test-access-token",
                RefreshToken = "test-refresh-token",
                JwtId = Guid.NewGuid().ToString()
            });

        var timeProvider = TimeProvider.System;
        var appDateTime = new AppDateTime(timeProvider);
        var timeBundle = new TimeBundle(appDateTime);

        var service = new AuthService(
            db,
            tokenServiceMock.Object,
            messengerOptions,
            jwtOptions,
            timeBundle,
            NullLogger<AuthService>.Instance);

        return (service, db);
    }

    [Fact]
    public async Task LoginAsync_ExistingUser_ReturnsSuccess()
    {
        var (service, db) = CreateService();
        await DbContextFactory.SeedUserAsync(db, "bob", "password123");

        var result = await service.LoginAsync("bob", "password123");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Username.Should().Be("bob");
        result.Value.Token.Should().Be("test-access-token");
        result.Value.Role.Should().Be(UserRole.User);
    }
    [Fact]
    public async Task RefreshTokenAsync_TokenAlreadyUsed_RevokesFamily()
    {
        var (service, db) = CreateService();
        var user = await DbContextFactory.SeedUserAsync(db, "carol", "pass123");

        const string familyId = "test-family-id";
        const string rawToken = "raw-refresh-token";
        var tokenHash = ITokenService.HashToken(rawToken);

        var usedToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            JwtId = "some-jti",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            UsedAt = DateTime.UtcNow.AddMinutes(-1),
            FamilyId = familyId
        };
        db.RefreshTokens.Add(usedToken);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "other-hash",
            JwtId = "other-jti",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(29),
            FamilyId = familyId
        });
        await db.SaveChangesAsync();

        var tokenServiceMock = new Mock<ITokenService>();
        tokenServiceMock.Setup(t => t.GetPrincipalFromExpiredToken(It.IsAny<string>()))
            .Returns(Result<ClaimsPrincipal>.Success(FakePrincipal(user.Id, "some-jti")));

        var result = await service.RefreshTokenAsync("any-access-token", rawToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Unauthorized);

        var familyTokens = db.RefreshTokens.Where(rt => rt.FamilyId == familyId).ToList();
        familyTokens.Should().AllSatisfy(t =>
            t.RevokedAt.Should().NotBeNull());
    }

    private static ClaimsPrincipal FakePrincipal(int userId, string jti)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti)
        };
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, "test"));
    }
}