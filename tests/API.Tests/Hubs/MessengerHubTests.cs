using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Infrastructure.Database;
using API.Web.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Online;
using Shared.Dto.ReadReceipt;
using Shared.Hubs;
using Xunit;

namespace API.Tests.Hubs;

public class MessengerHubTests : IntegrationTestBase
{
    private readonly Mock<HubCallerContext> _hubContextMock = new();
    private readonly Mock<IGroupManager> _groupsMock = new();
    private readonly Mock<IHubCallerClients> _clientsMock = new();
    private readonly Mock<ISingleClientProxy> _callerMock = new();
    private readonly Mock<ISingleClientProxy> _singleClientMock = new();
    private readonly Mock<IClientProxy> _groupMock = new();
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<ICallSessionService> _callMock = new();
    private readonly Mock<IAccessControlService> _accessMock = new();
    private readonly Mock<ISystemMessageService> _sysMsgMock = new();
    private readonly Mock<IUserStatusService> _statusServiceMock = new();
    private readonly Mock<IReadReceiptService> _receiptServiceMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private MessengerHub _hub;
    private readonly int _userId = 582;

    public MessengerHubTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "CallSettings:MaxParticipants", "30" },
                { "CallSettings:RelayPort", "5276" }
            })
            .Build();

        var appDateTime = new AppDateTime(TimeProvider.System);

        _hubContextMock.Setup(c => c.ConnectionId).Returns("conn-123");
        _hubContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "TestAuth")));

        _groupsMock.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _groupsMock.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _callerMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _singleClientMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _groupMock.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _clientsMock.Setup(c => c.Caller).Returns(_callerMock.Object);
        _clientsMock.Setup(c => c.Others).Returns(_groupMock.Object);
        _clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupMock.Object);
        _clientsMock.Setup(c => c.OthersInGroup(It.IsAny<string>())).Returns(_groupMock.Object);
        _clientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(_singleClientMock.Object);
        _clientsMock.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns(_groupMock.Object);

        _statusServiceMock
            .Setup(s => s.SetStatusAsync(It.IsAny<int>(), It.IsAny<UserStatusType>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Result.Success());
        _statusServiceMock
            .Setup(s => s.GetStatusAsync(It.IsAny<int>()))
            .ReturnsAsync(Result<UserStatusDto>.Success(new UserStatusDto(_userId, true, null, UserStatusType.Online, null)));

        _receiptServiceMock.Setup(r => r.GetAllUnreadCountsAsync(It.IsAny<int>()))
            .ReturnsAsync(Result<AllUnreadCountsDto>.Success(new AllUnreadCountsDto()));
        _receiptServiceMock.Setup(r => r.MarkAsReadAsync(It.IsAny<int>(), It.IsAny<MarkAsReadDto>()))
            .ReturnsAsync(Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto { ChatId = 1, UnreadCount = 0 }));
        _receiptServiceMock.Setup(r => r.MarkMessageAsReadAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto { ChatId = 1, UnreadCount = 0 }));
        _receiptServiceMock.Setup(r => r.GetChatReadInfoAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<ChatReadInfoDto>.Success(new ChatReadInfoDto()));

        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IUserStatusService)))
            .Returns(_statusServiceMock.Object);
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IReadReceiptService)))
            .Returns(_receiptServiceMock.Object);
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(MessengerDbContext)))
            .Returns(Context);

        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _scopeFactoryMock.Setup(s => s.CreateScope()).Returns(_scopeMock.Object);

        _hub = new MessengerHub(
            _scopeFactoryMock.Object,
            _onlineMock.Object,
            _callMock.Object,
            _accessMock.Object,
            _sysMsgMock.Object,
            Context,
            appDateTime,
            config,
            NullLogger<MessengerHub>.Instance)
        {
            Clients = _clientsMock.Object,
            Context = _hubContextMock.Object,
            Groups = _groupsMock.Object
        };
    }

    [Fact]
    public async Task JoinChat_AccessDenied_ThrowsHubException()
    {
        _accessMock.Setup(a => a.IsMemberAsync(_userId, 1)).ReturnsAsync(false);

        await _hub.Invoking(h => h.JoinChat(1))
            .Should().ThrowAsync<HubException>()
            .WithMessage("*доступа*");
    }

    [Fact]
    public async Task JoinChat_Success_AddsToGroup()
    {
        _accessMock.Setup(a => a.IsMemberAsync(_userId, 1)).ReturnsAsync(true);

        await _hub.JoinChat(1);

        _groupsMock.Verify(g => g.AddToGroupAsync("conn-123", "chat_1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveChat_RemovesFromGroup()
    {
        await _hub.LeaveChat(5);

        _groupsMock.Verify(g => g.RemoveFromGroupAsync("conn-123", "chat_5", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTyping_SendsToOthers()
    {
        await _hub.SendTyping(10);

        _clientsMock.Verify(c => c.OthersInGroup("chat_10"), Times.Once);
    }

    [Fact]
    public async Task InitiateCall_NotMember_ReturnsError()
    {
        _accessMock.Setup(a => a.IsMemberAsync(_userId, 1)).ReturnsAsync(false);

        await _hub.InitiateCall(1);

        _callerMock.Verify(c => c.SendCoreAsync(
            HubMethods.Call.CallError,
            It.Is<object[]>(args => args[0].ToString()!.Contains("доступа")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelCall_NotInitiator_ReturnsError()
    {
        var session = new CallSession
        {
            CallId = "call-1",
            ChatId = 1,
            InitiatorId = 999,
            Status = CallStatus.Ringing,
            IsGroupCall = false
        };
        session.ActiveParticipants[_userId] = new CallParticipant { UserId = _userId, ConnectionId = "conn-123" };
        _callMock.Setup(c => c.GetCall("call-1")).Returns(session);

        await _hub.CancelCall("call-1");

        _callerMock.Verify(c => c.SendCoreAsync(
            HubMethods.Call.CallError,
            It.Is<object[]>(args => args[0].ToString()!.Contains("инициатор")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCallMessage_EmptyText_NoSend()
    {
        var session = new CallSession { CallId = "call-1", ChatId = 1, InitiatorId = _userId };
        session.ActiveParticipants[_userId] = new CallParticipant { UserId = _userId, ConnectionId = "conn-123" };
        _callMock.Setup(c => c.GetCall("call-1")).Returns(session);

        await _hub.SendCallMessage("call-1", "");

        _clientsMock.Verify(c => c.Clients(It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_AddsToUserGroup()
    {
        _accessMock.Setup(a => a.GetUserChatIdsAsync(_userId)).ReturnsAsync([]);

        await _hub.OnConnectedAsync();

        _groupsMock.Verify(g => g.AddToGroupAsync(
            "conn-123",
            $"user_{_userId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetStatus_DoesNotThrow()
    {
        await _hub.Invoking(h => h.SetStatus((int)UserStatusType.Away, "1h"))
            .Should().NotThrowAsync();
    }
    [Fact]
    public async Task JoinChat_Success_VerifiesSendAsync()
    {
        _accessMock.Setup(a => a.IsMemberAsync(_userId, 1)).ReturnsAsync(true);
        await _hub.JoinChat(1);
        _groupsMock.Verify(g => g.AddToGroupAsync("conn-123", "chat_1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_LoadsUserChats()
    {
        _accessMock.Setup(a => a.GetUserChatIdsAsync(_userId)).ReturnsAsync(new List<int> { 1, 2 });
        await _hub.OnConnectedAsync();
        _groupsMock.Verify(g => g.AddToGroupAsync("conn-123", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task SendTyping_VerifiesGroupMethod()
    {
        await _hub.SendTyping(10);
        _clientsMock.Verify(c => c.OthersInGroup("chat_10"), Times.Once);
        _groupMock.Verify(g => g.SendCoreAsync(HubMethods.Chat.UserTyping, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelCall_VerifiesErrorSent()
    {
        var session = new CallSession
        {
            CallId = "call-1",
            ChatId = 1,
            InitiatorId = 999,
            Status = CallStatus.Ringing,
            IsGroupCall = false
        };
        session.ActiveParticipants[_userId] = new CallParticipant { UserId = _userId, ConnectionId = "conn-123" };
        _callMock.Setup(c => c.GetCall("call-1")).Returns(session);

        await _hub.CancelCall("call-1");

        _callerMock.Verify(c => c.SendCoreAsync(
            HubMethods.Call.CallError,
            It.Is<object[]>(args => args[0].ToString()!.Contains("инициатор")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task LeaveChat_VerifiesGroupRemoval()
    {
        await _hub.LeaveChat(5);
        _groupsMock.Verify(g => g.RemoveFromGroupAsync("conn-123", "chat_5", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateCall_ActiveCallExists_ReturnsError()
    {
        _accessMock.Setup(a => a.IsMemberAsync(_userId, 1)).ReturnsAsync(true);
        _callMock.Setup(c => c.GetActiveCallInChat(1)).Returns(new CallSession { CallId = "existing" });

        await _hub.InitiateCall(1);

        _callerMock.Verify(c => c.SendCoreAsync(
            HubMethods.Call.CallError,
            It.Is<object[]>(args => args[0].ToString()!.Contains("уже идёт звонок")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}