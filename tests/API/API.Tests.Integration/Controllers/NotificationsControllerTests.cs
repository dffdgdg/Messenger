using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Chat;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class NotificationsControllerTests : ControllerTestBase
{
    public NotificationsControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetChatSettings_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, Shared.Enum.ChatType.Chat, "Chat");

        var response = await Client.GetAsync($"/api/notifications/chat/{chat.Id}/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatNotificationSettingsDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetChatSettings_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, Shared.Enum.ChatType.Chat, "Private");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.GetAsync($"/api/notifications/chat/{chat.Id}/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetChatSettings_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/notifications/chat/1/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SetChatMute_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, Shared.Enum.ChatType.Chat, "Chat");

        var dto = new ChatNotificationSettingsDto
        {
            ChatId = chat.Id,
            NotificationsEnabled = false
        };

        var response = await Client.PostAsJsonAsync("/api/notifications/chat/mute", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatNotificationSettingsDto>>(JsonOptions);
        body!.Data!.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetChatMute_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, Shared.Enum.ChatType.Chat, "Private");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var dto = new ChatNotificationSettingsDto
        {
            ChatId = chat.Id,
            NotificationsEnabled = false
        };

        var response = await Client.PostAsJsonAsync("/api/notifications/chat/mute", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetChatMute_WhenUnauthorized_Returns401()
    {
        var dto = new ChatNotificationSettingsDto { ChatId = 1 };

        var response = await Client.PostAsJsonAsync("/api/notifications/chat/mute", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllSettings_WhenAuthorized_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, Shared.Enum.ChatType.Chat, "Chat");

        var response = await Client.GetAsync("/api/notifications/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChatNotificationSettingsDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllSettings_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/notifications/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}