using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.ReadReceipt;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class ReadReceiptsControllerTests : ControllerTestBase
{
    public ReadReceiptsControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task MarkAsRead_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("reader", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Hello");

        var dto = new MarkAsReadDto
        {
            ChatId = chat.Id,
            MessageId = message.Id
        };

        var response = await Client.PostAsJsonAsync("/api/read-receipts/mark-read", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ReadReceiptResponseDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data!.ChatId.Should().Be(chat.Id);
    }

    [Fact]
    public async Task MarkAsRead_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Private");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, owner.Id, "Hello");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var dto = new MarkAsReadDto { ChatId = chat.Id, MessageId = message.Id };

        var response = await Client.PostAsJsonAsync("/api/read-receipts/mark-read", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarkAsRead_WhenUnauthorized_Returns401()
    {
        var dto = new MarkAsReadDto { ChatId = 1, MessageId = 1 };

        var response = await Client.PostAsJsonAsync("/api/read-receipts/mark-read", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUnreadCount_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("reader", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var response = await Client.GetAsync($"/api/read-receipts/chat/{chat.Id}/unread-count");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<int>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetUnreadCount_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Private");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.GetAsync($"/api/read-receipts/chat/{chat.Id}/unread-count");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUnreadCount_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/read-receipts/chat/1/unread-count");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAllUnreadCounts_WhenAuthorized_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("reader", "Pass123!");
        var chat1 = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat A");
        var chat2 = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat B");

        var response = await Client.GetAsync("/api/read-receipts/unread-counts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AllUnreadCountsDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllUnreadCounts_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/read-receipts/unread-counts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}