using API.Common.Patterns;
using API.Configuration;
using API.Data;
using API.Hubs;
using API.Repositories.Abstarctions;
using API.Repositories.Projections;
using API.Services.Abstractions;
using API.Services.Chat;
using API.Services.Infrastructure.Bundles;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Shared.Dto.User;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class ChatServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IAccessControlService> _accessMock = new();
    private readonly Mock<IFileService> _fileMock = new();
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<ISystemMessageService> _sysMsgMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IReadReceiptService> _receiptMock = new();
    private readonly Mock<IHubContext<MessengerHub>> _hubContextMock = new();
    private readonly ChatService _service;

    public ChatServiceTests()
    {
        _context = DbContextFactory.Create();

        var cacheBundle = new CacheBundle(_accessMock.Object, _cacheMock.Object);
        var notifBundle = new NotificationBundle(_hubMock.Object, Mock.Of<INotificationService>());
        var timeBundle = new TimeBundle(new AppDateTime(TimeProvider.System));
        var chatBundle = new ChatBundle(_sysMsgMock.Object, cacheBundle, notifBundle, timeBundle);
        var mediaBundle = new MediaBundle(_fileMock.Object);
        var presenceBundle = new PresenceBundle(_onlineMock.Object);
        var urlBundle = new UrlBundle(_urlMock.Object);

        _service = new ChatService(
            _context,
            _chatRepoMock.Object,
            _userRepoMock.Object,
            chatBundle,
            mediaBundle,
            presenceBundle,
            urlBundle,
            _receiptMock.Object,
            _hubContextMock.Object,
            NullLogger<ChatService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetUserChats_NoChats_ReturnsEmpty()
    {
        _accessMock.Setup(a => a.GetUserChatIdsAsync(1)).ReturnsAsync([]);

        var result = await _service.GetUserChatsAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChatForUser_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.GetChatForUserAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task GetChatForUser_NotFound_ReturnsNotFound()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.FindByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Chat?)null);

        var result = await _service.GetChatForUserAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetUserDialogs_FiltersContactChats()
    {
        _accessMock.Setup(a => a.GetUserChatIdsAsync(1)).ReturnsAsync([]);
        _receiptMock.Setup(r => r.GetUnreadCountsForChatsAsync(1, It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

        var result = await _service.GetUserDialogsAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserGroups_FiltersNonContactChats()
    {
        _accessMock.Setup(a => a.GetUserChatIdsAsync(1)).ReturnsAsync([]);
        _receiptMock.Setup(r => r.GetUnreadCountsForChatsAsync(1, It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

        var result = await _service.GetUserGroupsAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetContactChat_NotFound_ReturnsNotFound()
    {
        _chatRepoMock.Setup(r => r.FindContactChatAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Chat?)null);

        var result = await _service.GetContactChatAsync(1, 2);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetChatMembers_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.GetChatMembersAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task CreateChat_InvalidCreator_ReturnsFailure()
    {
        var result = await _service.CreateChatAsync(new ChatDto { CreatedById = 0 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Некорректный ID");
    }

    [Fact]
    public async Task CreateChat_ContactNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.CreateChatAsync(new ChatDto
        {
            CreatedById = 1,
            Type = ChatType.Contact,
            Name = "5"
        });

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateChat_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.UpdateChatAsync(10, 1, new UpdateChatDto());

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task UpdateChat_NotFound_ReturnsNotFound()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.FindByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Chat?)null);

        var result = await _service.UpdateChatAsync(10, 1, new UpdateChatDto());

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateChat_ContactChat_ReturnsFailure()
    {
        var chat = new Chat { Id = 10, Type = ChatType.Contact, CreatedById = 1, CreatedAt = DateTime.UtcNow };
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.FindByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(chat);

        var result = await _service.UpdateChatAsync(10, 1, new UpdateChatDto { Name = "New" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Нельзя редактировать диалог");
    }

    [Fact]
    public async Task DeleteChat_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsOwnerAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.DeleteChatAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task DeleteChat_NotFound_ReturnsNotFound()
    {
        _accessMock.Setup(a => a.IsOwnerAsync(1, 10)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.FindByIdWithMembersAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Chat?)null);

        var result = await _service.DeleteChatAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UploadChatAvatar_NoFile_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(true);

        var result = await _service.UploadChatAvatarAsync(10, 1, null!);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не загружен");
    }

    [Fact]
    public async Task UploadChatAvatar_ContactChat_ReturnsFailure()
    {
        var chat = new Chat { Id = 10, Type = ChatType.Contact, CreatedById = 1, CreatedAt = DateTime.UtcNow };
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.ContentType).Returns("image/png");

        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.FindByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(chat);

        var result = await _service.UploadChatAvatarAsync(10, 1, fileMock.Object);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("диалога");
    }

    [Fact]
    public async Task RemoveChatAvatar_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 10)).ReturnsAsync(false);

        var result = await _service.RemoveChatAvatarAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }
}