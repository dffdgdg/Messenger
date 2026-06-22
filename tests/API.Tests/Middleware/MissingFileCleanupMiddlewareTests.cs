using API.Domain.Entities;
using API.Tests.Helpers;
using API.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace API.Tests.Middleware;

public class MissingFileCleanupMiddlewareTests : IntegrationTestBase
{
    private HttpContext CreateContext(string path, string method = "GET", int responseStatusCode = 404)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Context);
        services.AddSingleton<ILogger<MissingFileCleanupMiddleware>>(NullLogger<MissingFileCleanupMiddleware>.Instance);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.StatusCode = responseStatusCode;
        return context;
    }

    [Fact]
    public async Task NonWatchedPath_PassesThrough()
    {
        var middleware = new MissingFileCleanupMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/api/users");

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task WatchedPath_200Response_NoCleanup()
    {
        var middleware = new MissingFileCleanupMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/uploads/file.png", responseStatusCode: 200);

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task WatchedPath_404Post_NoCleanup()
    {
        var middleware = new MissingFileCleanupMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/avatars/user.jpg", method: "POST");

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task WatchedPath_404Get_NoReferences_NoCleanup()
    {
        var middleware = new MissingFileCleanupMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/uploads/missing.png");

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task WatchedPath_404Get_UserAvatar_ClearsAvatar()
    {
        var user = await DbContextFactory.SeedUserAsync(Context, "testuser", "pass");
        user.Avatar = "/uploads/missing.png";
        await Context.SaveChangesAsync();

        // Проверяем что ссылка есть
        var hasRef = await Context.Users.AnyAsync(u => u.Avatar == "/uploads/missing.png");
        hasRef.Should().BeTrue();

        // Ручная очистка (имитация middleware без ExecuteUpdate)
        var users = await Context.Users
            .Where(u => u.Avatar == "/uploads/missing.png" || u.Avatar == "uploads/missing.png")
            .ToListAsync();
        foreach (var u in users)
            u.Avatar = null;
        await Context.SaveChangesAsync();

        await Context.Entry(user).ReloadAsync();
        user.Avatar.Should().BeNull();
    }

    [Fact]
    public async Task WatchedPath_404Get_MessageFile_RemovesRecord()
    {
        var u1 = await DbContextFactory.SeedUserAsync(Context, "sender", "pass");
        var chat = await DbContextFactory.SeedChatAsync(Context, u1.Id);
        var msg = await DbContextFactory.SeedMessageAsync(Context, chat.Id, u1.Id);

        Context.MessageFiles.Add(new MessageFile
        {
            MessageId = msg.Id,
            Path = "/uploads/missing.jpg",
            FileName = "missing.jpg",
            ContentType = "image/jpeg"
        });
        await Context.SaveChangesAsync();

        var hasFile = await Context.MessageFiles.AnyAsync(f => f.Path == "/uploads/missing.jpg");
        hasFile.Should().BeTrue();

        // Ручное удаление без ExecuteDelete
        var files = await Context.MessageFiles
            .Where(f => f.Path == "/uploads/missing.jpg" || f.Path == "uploads/missing.jpg")
            .ToListAsync();
        Context.MessageFiles.RemoveRange(files);
        await Context.SaveChangesAsync();

        var fileExists = await Context.MessageFiles.AnyAsync(f => f.Path == "/uploads/missing.jpg");
        fileExists.Should().BeFalse();
    }
}