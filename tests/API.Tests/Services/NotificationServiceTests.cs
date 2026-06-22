using API.Application.Services.Abstractions;
using API.Application.Services.Features.Chat;
using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class NotificationServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);
        _service = new NotificationService(_unitOfWork, _chatRepoMock.Object, _hubMock.Object, _urlMock.Object, NullLogger<NotificationService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetChatNotificationSettings_NotMember_ReturnsFailure()
    {
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMember?)null);

        var result = await _service.GetChatNotificationSettingsAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не является участником");
    }

    [Fact]
    public async Task GetChatNotificationSettings_Member_ReturnsSettings()
    {
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMember { ChatId = 1, UserId = 10, NotificationsEnabled = false, JoinedAt = DateTime.UtcNow });

        var result = await _service.GetChatNotificationSettingsAsync(10, 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetChatMute_NotMember_ReturnsFailure()
    {
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMember?)null);

        var result = await _service.SetChatMuteAsync(10, new ChatNotificationSettingsDto { ChatId = 1, NotificationsEnabled = false });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SetChatMute_Member_UpdatesSetting()
    {
        var member = new ChatMember { ChatId = 1, UserId = 10, NotificationsEnabled = true, JoinedAt = DateTime.UtcNow };
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        var result = await _service.SetChatMuteAsync(10, new ChatNotificationSettingsDto { ChatId = 1, NotificationsEnabled = false });

        result.IsSuccess.Should().BeTrue();
        result.Value!.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllChatSettings_ReturnsList()
    {
        _chatRepoMock.Setup(r => r.GetMemberIdsForUserAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2]);

        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMember { ChatId = 1, UserId = 10, NotificationsEnabled = true, JoinedAt = DateTime.UtcNow });

        _chatRepoMock.Setup(r => r.GetMemberAsync(2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMember { ChatId = 2, UserId = 10, NotificationsEnabled = false, JoinedAt = DateTime.UtcNow });

        var result = await _service.GetAllChatSettingsAsync(10);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }
}