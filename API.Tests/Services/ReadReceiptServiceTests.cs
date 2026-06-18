using API.Data;
using API.Repositories.Abstarctions;
using API.Services.Infrastructure.Bundles;
using API.Services.ReadReceipt;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.ReadReceipt;
using Xunit;

namespace API.Tests.Services;

public class ReadReceiptServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IReadReceiptRepository> _repoMock = new();
    private readonly ReadReceiptService _service;

    public ReadReceiptServiceTests()
    {
        _context = DbContextFactory.Create();
        var timeBundle = new TimeBundle(new AppDateTime(TimeProvider.System));
        _service = new ReadReceiptService(_repoMock.Object, _context, timeBundle, NullLogger<ReadReceiptService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task MarkAsRead_NotMember_ReturnsFailure()
    {
        _repoMock.Setup(r => r.FindMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMember?)null);

        var result = await _service.MarkAsReadAsync(10, new MarkAsReadDto { ChatId = 1 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не является участником");
    }

    [Fact]
    public async Task MarkAsRead_MessageNotFound_ReturnsFailure()
    {
        var member = new ChatMember { ChatId = 1, UserId = 10, LastReadMessageId = 0 };
        _repoMock.Setup(r => r.FindMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _repoMock.Setup(r => r.MessageExistsAsync(999, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.MarkAsReadAsync(10, new MarkAsReadDto { ChatId = 1, MessageId = 999 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не найдено");
    }

    [Fact]
    public async Task MarkAsRead_NoMessageId_UsesLastMessage()
    {
        var member = new ChatMember { ChatId = 1, UserId = 10, LastReadMessageId = 0 };
        _repoMock.Setup(r => r.FindMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _repoMock.Setup(r => r.GetLastMessageIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        _repoMock.Setup(r => r.CountUnreadAsync(1, 10, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _service.MarkAsReadAsync(10, new MarkAsReadDto { ChatId = 1 });

        result.IsSuccess.Should().BeTrue();
        result.Value!.LastReadMessageId.Should().Be(42);
    }

    [Fact]
    public async Task MarkMessageAsRead_NotMember_ReturnsSuccessWithZero()
    {
        _repoMock.Setup(r => r.FindMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMember?)null);

        var result = await _service.MarkMessageAsReadAsync(10, 1, 5);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUnreadCount_NotMember_ReturnsZero()
    {
        _repoMock.Setup(r => r.FindMemberReadonlyAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMember?)null);

        var result = await _service.GetUnreadCountAsync(10, 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task GetUnreadCount_Member_ReturnsCount()
    {
        var member = new ChatMember { ChatId = 1, UserId = 10, LastReadMessageId = 3 };
        _repoMock.Setup(r => r.FindMemberReadonlyAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _repoMock.Setup(r => r.CountUnreadAsync(1, 10, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var result = await _service.GetUnreadCountAsync(10, 1);

        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task GetAllUnreadCounts_ReturnsTotal()
    {
        _repoMock.Setup(r => r.GetAllUnreadCountsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UnreadCountProjection(1, 3), new UnreadCountProjection(2, 5)]);

        var result = await _service.GetAllUnreadCountsAsync(10);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalUnread.Should().Be(8);
        result.Value.Chats.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarkAllAsRead_Success_ReturnsSuccess()
    {
        var member = new ChatMember { ChatId = 1, UserId = 10 };
        _repoMock.Setup(r => r.FindMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _repoMock.Setup(r => r.GetLastMessageIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);
        _repoMock.Setup(r => r.CountUnreadAsync(1, 10, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _service.MarkAllAsReadAsync(10, 1);

        result.IsSuccess.Should().BeTrue();
    }
}