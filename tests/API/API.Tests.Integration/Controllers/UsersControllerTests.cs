using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Online;
using Shared.Contracts.User;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class UsersControllerTests : ControllerTestBase
{
    public UsersControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetAllUsers_WhenAuthorized_Returns200()
    {
        await SeedAndAuthenticateAsync("user1", "Pass123!");
        await TestDataSeeder.SeedUserAsync(DbContext, "user2", "Pass123!");

        var response = await Client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAllUsers_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUser_WhenExists_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var response = await Client.GetAsync($"/api/users/{target.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOptions);
        body!.Data!.Username.Should().Be("target");
    }

    [Fact]
    public async Task GetUser_WhenNotFound_Returns404()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");

        var response = await Client.GetAsync("/api/users/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUser_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/users/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUser_WhenOwnProfile_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("editor", "Pass123!");

        var dto = new UserDto
        {
            Id = user.Id,
            Name = "NewName",
            Surname = "NewSurname"
        };

        var response = await Client.PutAsJsonAsync($"/api/users/{user.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateUser_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("editor", "Pass123!");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "other", "Pass123!");

        var dto = new UserDto { Id = other.Id, Name = "Hacked" };

        var response = await Client.PutAsJsonAsync($"/api/users/{other.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateUser_WhenUnauthorized_Returns401()
    {
        var dto = new UserDto { Id = 1, Name = "X" };

        var response = await Client.PutAsJsonAsync("/api/users/1", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadAvatar_WhenOwnProfile_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("avataruser", "Pass123!");

        var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");

        var response = await Client.PostAsync($"/api/users/{user.Id}/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AvatarResponseDto>>(JsonOptions);
        body!.Data!.AvatarUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadAvatar_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("avataruser", "Pass123!");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "other", "Pass123!");

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0xFF, 0xD8 }), "file", "photo.jpg");

        var response = await Client.PostAsync($"/api/users/{other.Id}/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadAvatar_WhenUnauthorized_Returns401()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0xFF, 0xD8 }), "file", "photo.jpg");

        var response = await Client.PostAsync("/api/users/1/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveAvatar_WhenOwnProfile_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("avataruser", "Pass123!");

        var response = await Client.DeleteAsync($"/api/users/{user.Id}/avatar");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveAvatar_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("avataruser", "Pass123!");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "other", "Pass123!");

        var response = await Client.DeleteAsync($"/api/users/{other.Id}/avatar");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveAvatar_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/users/1/avatar");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangeUsername_WhenOwnProfile_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("oldname", "Pass123!");

        var dto = new ChangeUsernameDto { NewUsername = "newname" };

        var response = await Client.PutAsJsonAsync($"/api/users/{user.Id}/username", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangeUsername_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("oldname", "Pass123!");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "other", "Pass123!");

        var dto = new ChangeUsernameDto { NewUsername = "hacked" };

        var response = await Client.PutAsJsonAsync($"/api/users/{other.Id}/username", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeUsername_WhenUnauthorized_Returns401()
    {
        var dto = new ChangeUsernameDto { NewUsername = "x" };

        var response = await Client.PutAsJsonAsync("/api/users/1/username", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WhenValid_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("passuser", "OldPass123!");

        var dto = new ChangePasswordDto
        {
            CurrentPassword = "OldPass123!",
            NewPassword = "NewPass456!"
        };

        var response = await Client.PutAsJsonAsync($"/api/users/{user.Id}/password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = "passuser",
            Password = "NewPass456!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WhenWrongCurrentPassword_Returns400()
    {
        var user = await SeedAndAuthenticateAsync("passuser2", "CorrectPass1!");

        var dto = new ChangePasswordDto
        {
            CurrentPassword = "WrongCurrentPass1!",
            NewPassword = "NewPass456!"
        };

        var response = await Client.PutAsJsonAsync($"/api/users/{user.Id}/password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("passuser", "Pass123!");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "other", "Pass123!");

        var dto = new ChangePasswordDto
        {
            CurrentPassword = "Pass123!",
            NewPassword = "NewPass456!"
        };

        var response = await Client.PutAsJsonAsync($"/api/users/{other.Id}/password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangePassword_WhenUnauthorized_Returns401()
    {
        var dto = new ChangePasswordDto { CurrentPassword = "x", NewPassword = "y" };

        var response = await Client.PutAsJsonAsync("/api/users/1/password", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOnlineUsers_WhenAuthorized_Returns200()
    {
        await SeedAndAuthenticateAsync("onlineViewer", "Pass123!");

        var response = await Client.GetAsync("/api/users/online");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OnlineUsersResponseDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetOnlineUsers_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/users/online");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserOnlineStatus_WhenAuthorized_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var response = await Client.GetAsync($"/api/users/{target.Id}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserStatusDto>>(JsonOptions);
        body!.Data!.UserId.Should().Be(target.Id);
    }

    [Fact]
    public async Task GetUserOnlineStatus_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/users/1/status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUsersOnlineStatus_WhenAuthorized_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        var user1 = await TestDataSeeder.SeedUserAsync(DbContext, "userA", "Pass123!");
        var user2 = await TestDataSeeder.SeedUserAsync(DbContext, "userB", "Pass123!");

        var userIds = new List<int> { user1.Id, user2.Id };

        var response = await Client.PostAsJsonAsync("/api/users/status/batch", userIds);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserStatusDto>>>(JsonOptions);
        body!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUsersOnlineStatus_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/users/status/batch", new List<int> { 1, 2 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}