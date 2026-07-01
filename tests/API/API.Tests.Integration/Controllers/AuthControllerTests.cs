using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Auth;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class AuthControllerTests : ControllerTestBase
{
    public AuthControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Login_WhenValidCredentials_Returns200WithTokens()
    {
        var password = "ValidP@ss123!";
        await TestDataSeeder.SeedUserAsync(DbContext, "logintest", password);

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("logintest", password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data!.Username.Should().Be("logintest");
        body.Data.Token.Should().NotBeNullOrEmpty();

        var setCookieHeader = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        setCookieHeader.Should().NotBeNull();
        setCookieHeader.Should().Contain("refresh_token=");
        setCookieHeader.Should().Contain("httponly");
        setCookieHeader.Should().Contain("path=/api/auth");
    }

    [Fact]
    public async Task Login_WhenWrongPassword_Returns401()
    {
        await TestDataSeeder.SeedUserAsync(DbContext, "logintest", "ValidP@ss123!");

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("logintest", "WrongPassword1!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        body!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Login_WhenUserNotFound_Returns404()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nonexistent", "Pass123!"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_WhenUserIsBanned_Returns403()
    {
        await TestDataSeeder.SeedUserAsync(DbContext, "banneduser", "Pass123!", isBanned: true);

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("banneduser", "Pass123!"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Login_WhenAdminUser_ReturnsTokenWithAdminRole()
    {
        var password = "AdminP@ss123!";
        await TestDataSeeder.SeedUserAsync(DbContext, "adminuser", password, departmentId: 1);

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("adminuser", password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        body!.Data!.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task Refresh_WhenValidTokens_Returns200WithNewTokens()
    {
        var password = "RefreshP@ss1!";
        await TestDataSeeder.SeedUserAsync(DbContext, "refreshuser", password);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("refreshuser", password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        var accessToken = loginBody!.Data!.Token;

        var setCookieHeaders = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", setCookieHeaders);
        refreshRequest.Content = JsonContent.Create(new RefreshTokenRequest(accessToken));

        var refreshResponse = await Client.SendAsync(refreshRequest);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponseDto>>(JsonOptions);
        refreshBody!.Success.Should().BeTrue();
        refreshBody.Data!.Token.Should().NotBeNullOrEmpty();
        refreshBody.Data.UserId.Should().Be(loginBody.Data.Id);

        var newSetCookie = refreshResponse.Headers.GetValues("Set-Cookie").FirstOrDefault();
        newSetCookie.Should().NotBeNull();
        newSetCookie.Should().Contain("refresh_token=");
    }

    [Fact]
    public async Task Refresh_WhenNoCookie_Returns401()
    {
        var password = "NoCookieP@ss1!";
        await TestDataSeeder.SeedUserAsync(DbContext, "nocookie", password);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nocookie", password));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>(JsonOptions);
        var accessToken = loginBody!.Data!.Token;

        var response = await Client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(accessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WhenInvalidAccessToken_Returns401()
    {
        var password = "BadTokenP@ss1!";
        await TestDataSeeder.SeedUserAsync(DbContext, "badtoken", password);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("badtoken", password));
        var setCookieHeaders = loginResponse.Headers.GetValues("Set-Cookie").ToList();

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", setCookieHeaders);
        refreshRequest.Content = JsonContent.Create(new RefreshTokenRequest("invalid_access_token_here"));

        var response = await Client.SendAsync(refreshRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_WhenAuthorized_Returns200AndClearsCookie()
    {
        var password = "RevokeP@ss1!";
        var user = await TestDataSeeder.SeedUserAsync(DbContext, "revokeuser", password);
        await AuthenticateAsync("revokeuser", password);

        var response = await Client.PostAsync("/api/auth/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeader = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        setCookieHeader.Should().NotBeNull();
        setCookieHeader!.Should().Contain("refresh_token");
        (setCookieHeader.Contains("refresh_token=;") || setCookieHeader.Contains("expires=Thu, 01 Jan 1970"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsync("/api/auth/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WhenManyAttempts_ShouldBeRateLimited()
    {
        var password = "RateP@ss123!";
        await TestDataSeeder.SeedUserAsync(DbContext, "ratetest", password);

        HttpResponseMessage? lastResponse = null;
        for (int i = 0; i < 20; i++)
        {
            lastResponse = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ratetest", "WrongPass1!"));
        }

        lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}