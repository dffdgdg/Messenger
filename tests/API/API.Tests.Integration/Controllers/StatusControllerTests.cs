using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Online;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class StatusControllerTests : ControllerTestBase
{
    public StatusControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task SetStatus_WhenValid_Returns200()
    {
        await SeedAndAuthenticateAsync("statususer", "Pass123!");

        var request = new SetStatusRequest
        {
            StatusType = UserStatusType.Away,
            Duration = "1h"
        };

        var response = await Client.PostAsJsonAsync("/api/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetStatus_WhenInvalidDuration_Returns400()
    {
        await SeedAndAuthenticateAsync("statususer", "Pass123!");

        var request = new SetStatusRequest
        {
            StatusType = UserStatusType.Away,
            Duration = "invalid_format"
        };

        var response = await Client.PostAsJsonAsync("/api/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetStatus_WhenUnauthorized_Returns401()
    {
        var request = new SetStatusRequest { StatusType = UserStatusType.Away, Duration = "1h" };

        var response = await Client.PostAsJsonAsync("/api/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrentStatus_WhenAuthorized_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("statususer", "Pass123!");

        var response = await Client.GetAsync("/api/status/current");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserStatusDto>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetCurrentStatus_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/status/current");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserStatus_WhenExists_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        var target = await TestDataSeeder.SeedUserAsync(DbContext, "target", "Pass123!");

        var response = await Client.GetAsync($"/api/status/user/{target.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserStatusDto>>(JsonOptions);
        body!.Data!.UserId.Should().Be(target.Id);
    }

    [Fact]
    public async Task GetUserStatus_WhenNotFound_Returns404()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");

        var response = await Client.GetAsync("/api/status/user/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUserStatus_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/status/user/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}