using System.Text.Json;
using API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Response;
using Xunit;

namespace API.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task NoException_PassesThrough()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");
        var middleware = new ExceptionHandlingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            env.Object);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Exception_Returns500()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Test error"),
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            env.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
        context.Response.ContentType.Should().Contain("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.Error.Should().Be("Произошла внутренняя ошибка");
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Exception_Development_IncludesDetails()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Dev error"),
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            env.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        response.Should().NotBeNull();
        response!.Details.Should().NotBeNull();
        response.Details.Should().Contain("InvalidOperationException");
        response.Details.Should().Contain("Dev error");
    }

    [Fact]
    public async Task Exception_Production_NoDetails()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Secret details"),
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            env.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        response.Should().NotBeNull();
        response!.Details.Should().BeNull();
    }
}