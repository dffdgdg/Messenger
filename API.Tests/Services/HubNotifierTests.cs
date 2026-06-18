using API.Hubs;
using API.Services.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class HubNotifierTests
{
    private readonly Mock<IHubContext<MessengerHub>> _hubMock = new();
    private readonly Mock<IClientProxy> _clientProxyMock = new();
    private readonly HubNotifier _notifier;

    public HubNotifierTests()
    {
        _clientProxyMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _notifier = new HubNotifier(_hubMock.Object, NullLogger<HubNotifier>.Instance);
    }

    [Fact]
    public async Task SendToChatAsync_SendsToGroup()
    {
        await _notifier.SendToChatAsync(5, "TestMethod", "arg1", "arg2");

        _clientProxyMock.Verify(c => c.SendCoreAsync(
            "TestMethod",
            It.Is<object[]>(args => args[0].ToString() == "arg1" && args[1].ToString() == "arg2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendToUserAsync_SendsToUserGroup()
    {
        await _notifier.SendToUserAsync(10, "TestMethod", "data");

        _clientProxyMock.Verify(c => c.SendCoreAsync(
            "TestMethod",
            It.Is<object[]>(args => args[0].ToString() == "data"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendToChatAsync_Exception_DoesNotThrow()
    {
        _clientProxyMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection lost"));

        await _notifier.Invoking(n => n.SendToChatAsync(1, "Test"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendToUserAsync_Exception_DoesNotThrow()
    {
        _clientProxyMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection lost"));

        await _notifier.Invoking(n => n.SendToUserAsync(1, "Test"))
            .Should().NotThrowAsync();
    }
}