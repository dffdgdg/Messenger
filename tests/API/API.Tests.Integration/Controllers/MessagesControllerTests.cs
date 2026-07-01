using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Message;
using Shared.Contracts.Search;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class MessagesControllerTests : ControllerTestBase
{
    public MessagesControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CreateMessage_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("sender", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var request = new CreateMessageRequest
        {
            ChatId = chat.Id,
            Content = "Hello!"
        };

        var response = await Client.PostAsJsonAsync("/api/messages", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data!.Content.Should().Be("Hello!");
    }

    [Fact]
    public async Task CreateMessage_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Private");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.PostAsJsonAsync("/api/messages", new CreateMessageRequest
        {
            ChatId = chat.Id,
            Content = "Should fail"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateMessage_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/messages", new CreateMessageRequest
        {
            ChatId = 1,
            Content = "X"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateMessage_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("author", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Original");

        var dto = new UpdateMessageDto { Id = message.Id, Content = "Updated" };

        var response = await Client.PutAsJsonAsync($"/api/messages/{message.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>(JsonOptions);
        body!.Data!.Content.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateMessage_IdMismatch_Returns400()
    {
        await SeedAndAuthenticateAsync("author", "Pass123!");

        var dto = new UpdateMessageDto { Id = 1, Content = "X" };

        var response = await Client.PutAsJsonAsync("/api/messages/999", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMessage_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, owner.Id, "Original");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var dto = new UpdateMessageDto { Id = message.Id, Content = "Hacked" };

        var response = await Client.PutAsJsonAsync($"/api/messages/{message.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateMessage_WhenUnauthorized_Returns401()
    {
        var dto = new UpdateMessageDto { Id = 1, Content = "X" };

        var response = await Client.PutAsJsonAsync("/api/messages/1", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteMessage_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("author", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "ToDelete");

        var response = await Client.DeleteAsync($"/api/messages/{message.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMessage_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, owner.Id, "Original");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.DeleteAsync($"/api/messages/{message.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteMessage_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/messages/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PinMessage_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Pin me");

        var response = await Client.PostAsync($"/api/messages/{message.Id}/pin", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PinMessage_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsync("/api/messages/1/pin", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnpinMessage_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Unpin me");

        await Client.PostAsync($"/api/messages/{message.Id}/pin", null);

        var response = await Client.DeleteAsync($"/api/messages/{message.Id}/pin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnpinMessage_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/messages/1/pin");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPinnedMessages_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var message = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Pinned");
        await Client.PostAsync($"/api/messages/{message.Id}/pin", null);

        var response = await Client.GetAsync($"/api/messages/chat/{chat.Id}/pinned");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<MessageDto>>>(JsonOptions);
        body!.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPinnedMessages_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/messages/chat/1/pinned");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLatestMessages_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Msg1");
        await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Msg2");

        var response = await Client.GetAsync($"/api/messages/chat/{chat.Id}/latest?take=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedMessagesDto>>(JsonOptions);
        body!.Data!.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLatestMessages_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/messages/chat/1/latest");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMessagesAround_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var msg1 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Before");
        var msg2 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Center");
        var msg3 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "After");

        var response = await Client.GetAsync($"/api/messages/chat/{chat.Id}/around/{msg2.Id}?count=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedMessagesDto>>(JsonOptions);
        body!.Data!.Messages.Should().Contain(m => m.Id == msg1.Id);
        body.Data.Messages.Should().Contain(m => m.Id == msg2.Id);
        body.Data.Messages.Should().Contain(m => m.Id == msg3.Id);
    }

    [Fact]
    public async Task GetMessagesAround_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/messages/chat/1/around/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMessagesBefore_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var msg1 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Old");
        var msg2 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Newer");

        var response = await Client.GetAsync($"/api/messages/chat/{chat.Id}/before/{msg2.Id}?count=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedMessagesDto>>(JsonOptions);
        body!.Data!.Messages.Should().Contain(m => m.Id == msg1.Id);
    }

    [Fact]
    public async Task GetMessagesBefore_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/messages/chat/1/before/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMessagesAfter_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        var msg1 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Old");
        var msg2 = await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Newer");

        var response = await Client.GetAsync($"/api/messages/chat/{chat.Id}/after/{msg1.Id}?count=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedMessagesDto>>(JsonOptions);
        body!.Data!.Messages.Should().Contain(m => m.Id == msg2.Id);
    }

    [Fact]
    public async Task GetMessagesAfter_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/messages/chat/1/after/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SearchMessages_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Hello world");

        var query = new SearchMessagesQueryDto { Query = "Hello", Page = 1, PageSize = 10 };

        var response = await Client.PostAsJsonAsync($"/api/messages/chat/{chat.Id}/search", query);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchMessages_WhenUnauthorized_Returns401()
    {
        var query = new SearchMessagesQueryDto { Query = "test" };

        var response = await Client.PostAsJsonAsync("/api/messages/chat/1/search", query);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetChatCounts_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");
        await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Msg");

        var response = await Client.GetAsync($"/api/messages/chat/{chat.Id}/counts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatCountsDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetChatCounts_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/messages/chat/1/counts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GlobalSearch_WhenOwnUserId_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("searcher", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "My Chat");
        await TestDataSeeder.SeedMessageAsync(DbContext, chat.Id, user.Id, "Hello from chat");

        var query = new GlobalSearchQueryDto { Query = "Hello" };

        var response = await Client.PostAsJsonAsync($"/api/messages/user/{user.Id}/search", query);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GlobalSearch_WhenOtherUserId_Returns403()
    {
        await SeedAndAuthenticateAsync("searcher", "Pass123!");

        var query = new GlobalSearchQueryDto { Query = "test" };

        var response = await Client.PostAsJsonAsync("/api/messages/user/99999/search", query);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GlobalSearch_WhenUnauthorized_Returns401()
    {
        var query = new GlobalSearchQueryDto { Query = "test" };

        var response = await Client.PostAsJsonAsync("/api/messages/user/1/search", query);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}