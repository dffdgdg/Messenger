using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Chat;
using Shared.Contracts.User;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class ChatsControllerTests : ControllerTestBase
{
    public ChatsControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetChat_WhenAuthorizedAndMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("chat_member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "My Chat");

        var response = await Client.GetAsync($"/api/chats/{chat.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data!.Id.Should().Be(chat.Id);
        body.Data.Name.Should().Be("My Chat");
    }

    [Fact]
    public async Task GetChat_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetChat_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Private");

        var intruder = await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.GetAsync($"/api/chats/{chat.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetChat_WhenNotExists_Returns404()
    {
        await SeedAndAuthenticateAsync("seeker", "Pass123!");

        var response = await Client.GetAsync("/api/chats/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUserChats_WhenOwnChats_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("u1", "Pass123!");
        await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat A");
        await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat B");

        var response = await Client.GetAsync($"/api/chats/user/{user.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChatDto>>>(JsonOptions);
        body!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUserChats_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("u1", "Pass123!");

        var response = await Client.GetAsync("/api/chats/user/99999");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserChats_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/user/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserDialogs_WhenOwnDialogs_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("u1", "Pass123!");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "u2", "Pass123!");
        await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Contact, "Dialog", new[] { other.Id });

        var response = await Client.GetAsync($"/api/chats/user/{user.Id}/dialogs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChatDto>>>(JsonOptions);
        body!.Data.Should().ContainSingle(c => c.Type == ChatType.Contact);
    }

    [Fact]
    public async Task GetUserDialogs_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("u1", "Pass123!");

        var response = await Client.GetAsync("/api/chats/user/999/dialogs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserDialogs_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/user/1/dialogs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserGroups_WhenOwnGroups_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("u1", "Pass123!");
        await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Group A");
        var other = await TestDataSeeder.SeedUserAsync(DbContext, "u2", "Pass123!");
        await TestDataSeeder.SeedChatAsync(DbContext, other.Id, ChatType.Contact, "Dialog", new[] { user.Id });

        var response = await Client.GetAsync($"/api/chats/user/{user.Id}/groups");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChatDto>>>(JsonOptions);
        body!.Data.Should().Contain(c => c.Type == ChatType.Chat);
        body!.Data.Should().NotContain(c => c.Type == ChatType.Contact);
    }

    [Fact]
    public async Task GetUserGroups_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("u1", "Pass123!");

        var response = await Client.GetAsync("/api/chats/user/999/groups");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserGroups_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/user/1/groups");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetContactChat_WhenExists_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("u1", "Pass123!");
        var contact = await TestDataSeeder.SeedUserAsync(DbContext, "u2", "Pass123!");

        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Contact, null, new[] { contact.Id });

        var response = await Client.GetAsync($"/api/chats/user/{user.Id}/contact/{contact.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatDto>>(JsonOptions);
        body!.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetContactChat_WhenOtherUser_Returns403()
    {
        await SeedAndAuthenticateAsync("u1", "Pass123!");

        var response = await Client.GetAsync("/api/chats/user/999/contact/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetContactChat_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/user/1/contact/2");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMembers_WhenAuthorized_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var member = await TestDataSeeder.SeedUserAsync(DbContext, "member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat", new[] { member.Id });

        var response = await Client.GetAsync($"/api/chats/{chat.Id}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserDto>>>(JsonOptions);
        body!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMembers_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/1/members");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMembersDetailed_WhenAuthorized_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var member = await TestDataSeeder.SeedUserAsync(DbContext, "member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat", new[] { member.Id });

        var response = await Client.GetAsync($"/api/chats/{chat.Id}/members/detailed");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChatMemberDto>>>(JsonOptions);
        body!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMembersDetailed_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/chats/1/members/detailed");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddMember_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var newMember = await TestDataSeeder.SeedUserAsync(DbContext, "newguy", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var response = await Client.PostAsJsonAsync($"/api/chats/{chat.Id}/members", new UpdateChatMemberDto
        {
            UserId = newMember.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddMember_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var newMember = await TestDataSeeder.SeedUserAsync(DbContext, "newguy", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.PostAsJsonAsync($"/api/chats/{chat.Id}/members", new UpdateChatMemberDto
        {
            UserId = newMember.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddMember_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/chats/1/members", new UpdateChatMemberDto { UserId = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveMember_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var member = await TestDataSeeder.SeedUserAsync(DbContext, "member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat", new[] { member.Id });

        var response = await Client.DeleteAsync($"/api/chats/{chat.Id}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveMember_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var member = await TestDataSeeder.SeedUserAsync(DbContext, "member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat", new[] { member.Id });
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.DeleteAsync($"/api/chats/{chat.Id}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveMember_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/chats/1/members/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateMemberRole_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var member = await TestDataSeeder.SeedUserAsync(DbContext, "member", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat", new[] { member.Id });

        var response = await Client.PutAsync($"/api/chats/{chat.Id}/members/{member.Id}/role?role={ChatRole.Admin}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMemberRole_WhenUnauthorized_Returns401()
    {
        var response = await Client.PutAsync("/api/chats/1/members/1/role?role=Admin", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateChat_WhenValid_Returns200()
    {
        await SeedAndAuthenticateAsync("creator", "Pass123!");

        var dto = new ChatDto
        {
            Name = "New Chat",
            Type = ChatType.Chat,
            ShowHistoryForNewMembers = false
        };

        var response = await Client.PostAsJsonAsync("/api/chats", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatDto>>(JsonOptions);
        body!.Data!.Name.Should().Be("New Chat");
        body.Data.Type.Should().Be(ChatType.Chat);
        body.Data.ShowHistoryForNewMembers.Should().BeFalse();
    }

    [Fact]
    public async Task CreateChat_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/chats", new ChatDto { Name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateChat_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Old Name");

        var dto = new UpdateChatDto
        {
            Name = "Updated Name",
            ChatType = ChatType.Chat,
            ShowHistoryForNewMembers = false
        };

        var response = await Client.PutAsJsonAsync($"/api/chats/{chat.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatDto>>(JsonOptions);
        body!.Data!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateChat_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var dto = new UpdateChatDto { Name = "Hacked" };

        var response = await Client.PutAsJsonAsync($"/api/chats/{chat.Id}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateChat_WhenUnauthorized_Returns401()
    {
        var response = await Client.PutAsJsonAsync("/api/chats/1", new UpdateChatDto { Name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteChat_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "ToDelete");

        var response = await Client.DeleteAsync($"/api/chats/{chat.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteChat_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.DeleteAsync($"/api/chats/{chat.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteChat_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/chats/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadAvatar_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }; // JPEG header
        content.Add(new ByteArrayContent(fileBytes), "file", "avatar.jpg");

        var response = await Client.PostAsync($"/api/chats/{chat.Id}/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadAvatar_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0xFF, 0xD8 }), "file", "avatar.jpg");

        var response = await Client.PostAsync($"/api/chats/{chat.Id}/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadAvatar_WhenUnauthorized_Returns401()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0xFF, 0xD8 }), "file", "avatar.jpg");

        var response = await Client.PostAsync("/api/chats/1/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveAvatar_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var response = await Client.DeleteAsync($"/api/chats/{chat.Id}/avatar");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveAvatar_WhenNotOwner_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Chat");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var response = await Client.DeleteAsync($"/api/chats/{chat.Id}/avatar");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveAvatar_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/chats/1/avatar");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}