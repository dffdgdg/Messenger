using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.User;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class AdminControllerTests : ControllerTestBase
{
    public AdminControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task AuthenticateAsAdminAsync(string username = "admin", string password = "AdminP@ss123!")
    {
        await TestDataSeeder.SeedUserAsync(DbContext, username, password, departmentId: 1);
        await AuthenticateAsync(username, password);
    }

    [Fact]
    public async Task GetUsers_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetUsers_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");

        var response = await Client.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();

        var dto = new CreateUserDto
        {
            Username = "newuser",
            Password = "StrongP@ss1",
            Name = "New",
            Surname = "User"
        };

        var response = await Client.PostAsJsonAsync("/api/admin/users", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOptions);
        body!.Data!.Username.Should().Be("newuser");

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = "newuser",
            Password = "StrongP@ss1"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateUser_WhenDuplicateUsername_Returns409()
    {
        await AuthenticateAsAdminAsync();
        await TestDataSeeder.SeedUserAsync(DbContext, "existing", "Pass123!");

        var dto = new CreateUserDto
        {
            Username = "existing",
            Password = "StrongP@ss1",
            Name = "Dup",
            Surname = "User"
        };

        var response = await Client.PostAsJsonAsync("/api/admin/users", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateUser_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");

        var dto = new CreateUserDto { Username = "x", Password = "Pass123!", Name = "X", Surname = "Y" };

        var response = await Client.PostAsJsonAsync("/api/admin/users", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/admin/users",
            new CreateUserDto { Username = "x", Password = "Pass123!", Name = "X", Surname = "Y" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUser_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var dto = new UserDto { Id = target.Id, Name = "UpdatedName", Surname = "UpdatedSurname" };

        var response = await Client.PutAsJsonAsync($"/api/admin/users/{target.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateUser_WhenNotFound_Returns404()
    {
        await AuthenticateAsAdminAsync();

        var dto = new UserDto { Id = 99999, Name = "Ghost", Surname = "Ghost" };

        var response = await Client.PutAsJsonAsync("/api/admin/users/99999", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateUser_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var response = await Client.PutAsJsonAsync($"/api/admin/users/{target.Id}",
            new UserDto { Id = target.Id, Name = "X", Surname = "Y" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateUser_WhenUnauthorized_Returns401()
    {
        var response = await Client.PutAsJsonAsync("/api/admin/users/1",
            new UserDto { Id = 1, Name = "X", Surname = "Y" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ToggleBan_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var response = await Client.PostAsync($"/api/admin/users/{target.Id}/toggle-ban", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = "target",
            Password = "Pass123!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ToggleBan_WhenNotFound_Returns404()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsync("/api/admin/users/99999/toggle-ban", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ToggleBan_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var response = await Client.PostAsync($"/api/admin/users/{target.Id}/toggle-ban", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ToggleBan_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsync("/api/admin/users/1/toggle-ban", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResetPassword_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "OldPass123!");

        var dto = new ResetPasswordAdminDto { NewPassword = "NewSecureP@ss1!" };

        var response = await Client.PostAsJsonAsync($"/api/admin/users/{target.Id}/reset-password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = "target",
            Password = "NewSecureP@ss1!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_WhenNotFound_Returns404()
    {
        await AuthenticateAsAdminAsync();

        var dto = new ResetPasswordAdminDto { NewPassword = "NewSecureP@ss1!" };

        var response = await Client.PostAsJsonAsync("/api/admin/users/99999/reset-password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetPassword_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var dto = new ResetPasswordAdminDto { NewPassword = "NewSecureP@ss1!" };

        var response = await Client.PostAsJsonAsync($"/api/admin/users/{target.Id}/reset-password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetPassword_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/admin/users/1/reset-password",
            new ResetPasswordAdminDto { NewPassword = "Pass123!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}