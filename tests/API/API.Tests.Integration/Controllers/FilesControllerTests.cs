using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Message;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class FilesControllerTests : ControllerTestBase
{
    public FilesControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Upload_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("uploader", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, Shared.Enum.ChatType.Chat, "Chat");

        var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        content.Add(new ByteArrayContent(fileBytes), "file", "test.png");

        var response = await Client.PostAsync($"/api/files/upload?chatId={chat.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MessageFileDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, Shared.Enum.ChatType.Chat, "Private");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "test.png");

        var response = await Client.PostAsync($"/api/files/upload?chatId={chat.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upload_WhenUnauthorized_Returns401()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "test.png");

        var response = await Client.PostAsync("/api/files/upload?chatId=1", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_WhenFileTooLarge_Returns400()
    {
        var user = await SeedAndAuthenticateAsync("uploader", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, Shared.Enum.ChatType.Chat, "Chat");

        var bigFile = new byte[100 * 1024 * 1024];
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bigFile), "file", "bigfile.bin");

        var response = await Client.PostAsync($"/api/files/upload?chatId={chat.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MessageFileDto>>(JsonOptions);
        body!.Success.Should().BeFalse();
    }
}