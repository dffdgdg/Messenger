using API.Infrastructure.Status;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace API.Tests.Services;

public class OnlineUserServiceTests : IDisposable
{
    private readonly OnlineUserService _service;

    public OnlineUserServiceTests()
    {
        _service = new OnlineUserService(NullLogger<OnlineUserService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void UserConnected_AddsToOnlineList()
    {
        _service.UserConnected(1, "conn-1");

        _service.IsOnline(1).Should().BeTrue();
        _service.OnlineCount.Should().Be(1);
    }

    [Fact]
    public void UserDisconnected_LastConnection_GoesOffline()
    {
        _service.UserConnected(1, "conn-1");
        _service.UserDisconnected(1, "conn-1");

        _service.IsOnline(1).Should().BeFalse();
    }

    [Fact]
    public void UserDisconnected_MultipleConnections_StaysOnline()
    {
        _service.UserConnected(1, "conn-1");
        _service.UserConnected(1, "conn-2");
        _service.UserDisconnected(1, "conn-1");

        _service.IsOnline(1).Should().BeTrue();
    }

    [Fact]
    public void GetOnlineUserIds_ReturnsOnlyOnline()
    {
        _service.UserConnected(1, "conn-1");
        _service.UserConnected(2, "conn-2");
        _service.UserDisconnected(2, "conn-2");

        var ids = _service.GetOnlineUserIds();

        ids.Should().Contain(1);
        ids.Should().NotContain(2);
    }

    [Fact]
    public void FilterOnline_ReturnsOnlyOnlineIds()
    {
        _service.UserConnected(1, "conn-1");
        _service.UserConnected(3, "conn-3");

        var result = _service.FilterOnline([1, 2, 3]);

        result.Should().Equal([1, 3]);
    }

    [Fact]
    public void GetConnectionIds_ReturnsAllConnections()
    {
        _service.UserConnected(1, "conn-a");
        _service.UserConnected(1, "conn-b");

        var ids = _service.GetConnectionIds(1);

        ids.Should().HaveCount(2);
        ids.Should().Contain(["conn-a", "conn-b"]);
    }
}