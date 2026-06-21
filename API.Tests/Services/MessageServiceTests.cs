using API.Common.Patterns;
using API.Configuration;
using API.Repositories.Abstarctions;
using API.Services.Infrastructure.Bundles;
using API.Services.Messaging;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.Message;
using Shared.Dto.Search;
using Shared.DTO.Message;
using Xunit;

namespace API.Tests.Services;

public class MessageServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IAccessControlService> _accessMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<INotificationService> _notifMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly Mock<IFileService> _fileMock = new();
    private readonly Mock<IReadReceiptService> _receiptMock = new();
    private readonly Mock<ISystemMessageService> _sysMsgMock = new();
    private readonly MessageService _service;

    public MessageServiceTests()
    {
        _context = DbContextFactory.Create();

        var settings = Options.Create(new MessengerSettings { MaxPageSize = 100 });

        var cacheBundle = new CacheBundle(_accessMock.Object, Mock.Of<ICacheService>());
        var notifBundle = new NotificationBundle(_hubMock.Object, _notifMock.Object);
        var timeBundle = new TimeBundle(new AppDateTime(TimeProvider.System));
        var chatBundle = new ChatBundle(_sysMsgMock.Object, cacheBundle, notifBundle, timeBundle);
        var mediaBundle = new MediaBundle(_fileMock.Object);
        var urlBundle = new UrlBundle(_urlMock.Object);

        _service = new MessageService(
            _context,
            _chatRepoMock.Object,
            _msgRepoMock.Object,
            chatBundle,
            mediaBundle,
            urlBundle,
            _receiptMock.Object,
            _sysMsgMock.Object,
            settings,
            logger: null,
            NullLogger<MessageService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ==================== Create ====================

    [Fact]
    public async Task CreateMessage_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);
        var result = await _service.CreateMessageAsync(1, new CreateMessageRequest { ChatId = 10 });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task CreateMessage_EmptyContent_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.CreateMessageAsync(1, new CreateMessageRequest { ChatId = 10 });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("текст или файлы");
    }

    [Fact]
    public async Task CreateMessage_ReplyNotFound_ReturnsNotFound()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        _msgRepoMock.Setup(r => r.ExistsInChatAsync(999, 10, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await _service.CreateMessageAsync(1, new CreateMessageRequest { ChatId = 10, Content = "Hello", ReplyToMessageId = 999 });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task CreateMessage_VoiceWithoutFile_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.CreateMessageAsync(1, new CreateMessageRequest { ChatId = 10, IsVoiceMessage = true });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("аудиофайл");
    }

    // ==================== Update ====================

    [Fact]
    public async Task UpdateMessage_NotFound_ReturnsNotFound()
    {
        _msgRepoMock.Setup(r => r.FindUserMessageWithIncludesAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserMessage?)null);
        var result = await _service.UpdateMessageAsync(999, 1, new UpdateMessageDto { Id = 999, Content = "New" });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateMessage_NotSender_ReturnsForbidden()
    {
        var msg = CreateMessage(chatId: 10, senderId: 999, content: "Old");
        _msgRepoMock.Setup(r => r.FindUserMessageWithIncludesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.UpdateMessageAsync(1, 1, new UpdateMessageDto { Id = 1, Content = "New" });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task UpdateMessage_IsDeleted_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, content: "Old", isDeleted: true);
        _msgRepoMock.Setup(r => r.FindUserMessageWithIncludesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.UpdateMessageAsync(1, 1, new UpdateMessageDto { Id = 1, Content = "New" });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("удалено");
    }

    [Fact]
    public async Task UpdateMessage_HasPoll_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, content: "Old", poll: new Poll());
        _msgRepoMock.Setup(r => r.FindUserMessageWithIncludesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.UpdateMessageAsync(1, 1, new UpdateMessageDto { Id = 1, Content = "New" });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("опросом");
    }

    [Fact]
    public async Task UpdateMessage_EmptyContent_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, content: "Old");
        _msgRepoMock.Setup(r => r.FindUserMessageWithIncludesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.UpdateMessageAsync(1, 1, new UpdateMessageDto { Id = 1, Content = "" });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("пустым");
    }

    // ==================== Delete ====================

    [Fact]
    public async Task DeleteMessage_NotFound_ReturnsNotFound()
    {
        _msgRepoMock.Setup(r => r.FindUserMessageForDeleteAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserMessage?)null);
        var result = await _service.DeleteMessageAsync(999, 1);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteMessage_NotSenderNotAdmin_ReturnsForbidden()
    {
        var msg = CreateMessage(chatId: 10, senderId: 999);
        _msgRepoMock.Setup(r => r.FindUserMessageForDeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(false);
        var result = await _service.DeleteMessageAsync(1, 1);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task DeleteMessage_AlreadyDeleted_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, isDeleted: true);
        _msgRepoMock.Setup(r => r.FindUserMessageForDeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.DeleteMessageAsync(1, 1);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("удалено");
    }

    [Fact]
    public async Task DeleteMessage_AdminCanDeleteOthers()
    {
        var msg = CreateMessage(chatId: 10, senderId: 999);
        _msgRepoMock.Setup(r => r.FindUserMessageForDeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(true);
        _msgRepoMock.Setup(r => r.SoftDeleteAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var result = await _service.DeleteMessageAsync(1, 1);
        result.IsSuccess.Should().BeTrue();
    }

    // ==================== Pin ====================

    [Fact]
    public async Task PinMessage_NotFound_ReturnsNotFound()
    {
        _msgRepoMock.Setup(r => r.FindUserMessageByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserMessage?)null);
        var result = await _service.PinMessageAsync(999, 1);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task PinMessage_IsDeleted_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, isDeleted: true);
        _msgRepoMock.Setup(r => r.FindUserMessageByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.PinMessageAsync(1, 1);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("удаленное");
    }

    [Fact]
    public async Task PinMessage_AlreadyPinned_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, pinnedAt: DateTime.UtcNow);
        _msgRepoMock.Setup(r => r.FindUserMessageByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.PinMessageAsync(1, 1);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("закреплено");
    }

    // ==================== Unpin ====================

    [Fact]
    public async Task UnpinMessage_NotPinned_ReturnsFailure()
    {
        var msg = CreateMessage(chatId: 10, senderId: 1, pinnedAt: null);
        _msgRepoMock.Setup(r => r.FindUserMessageByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(msg);
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var result = await _service.UnpinMessageAsync(1, 1);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не закреплено");
    }

    // ==================== Get Messages ====================

    [Fact]
    public async Task GetLatestMessages_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);
        var result = await _service.GetLatestMessagesAsync(10, 1, 50);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task GetPinnedMessages_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);
        var result = await _service.GetPinnedMessagesAsync(10, 1);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task SearchMessages_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);
        var result = await _service.SearchMessagesAsync(10, 1, new SearchMessagesQueryDto());
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task GlobalSearch_NoChats_ReturnsEmpty()
    {
        _accessMock.Setup(a => a.GetUserChatIdsAsync(1)).ReturnsAsync([]);
        var result = await _service.GlobalSearchAsync(1, new GlobalSearchQueryDto());
        result.IsSuccess.Should().BeTrue();
        result.Value!.Messages.Should().BeEmpty();
        result.Value.Chats.Should().BeEmpty();
    }

    private static UserMessage CreateMessage(int chatId = 10, int senderId = 1, string content = "Test",
        bool isDeleted = false, DateTime? pinnedAt = null, Poll? poll = null)
    {
        return new UserMessage
        {
            Id = 1,
            ChatId = chatId,
            SenderId = senderId,
            Content = content,
            IsDeleted = isDeleted,
            PinnedAt = pinnedAt,
            Poll = poll
        };
    }
}