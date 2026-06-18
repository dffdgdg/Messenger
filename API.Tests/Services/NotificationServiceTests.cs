using API.Data;
using API.Services.Abstractions;
using API.Services.Chat;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Xunit;

namespace API.Tests.Services;

public class NotificationServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _context = DbContextFactory.Create();
        _service = new NotificationService(_context, _hubMock.Object, _urlMock.Object, NullLogger<NotificationService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetChatNotificationSettings_NotMember_ReturnsFailure()
    {
        var result = await _service.GetChatNotificationSettingsAsync(10, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не является участником");
    }

    [Fact]
    public async Task GetChatNotificationSettings_Member_ReturnsSettings()
    {
        _context.ChatMembers.Add(new ChatMember { ChatId = 1, UserId = 10, NotificationsEnabled = false, JoinedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _service.GetChatNotificationSettingsAsync(10, 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetChatMute_NotMember_ReturnsFailure()
    {
        var result = await _service.SetChatMuteAsync(10, new ChatNotificationSettingsDto { ChatId = 1, NotificationsEnabled = false });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SetChatMute_Member_UpdatesSetting()
    {
        _context.ChatMembers.Add(new ChatMember { ChatId = 1, UserId = 10, NotificationsEnabled = true, JoinedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _service.SetChatMuteAsync(10, new ChatNotificationSettingsDto { ChatId = 1, NotificationsEnabled = false });

        result.IsSuccess.Should().BeTrue();
        result.Value!.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllChatSettings_ReturnsList()
    {
        _context.ChatMembers.Add(new ChatMember { ChatId = 1, UserId = 10, NotificationsEnabled = true, JoinedAt = DateTime.UtcNow });
        _context.ChatMembers.Add(new ChatMember { ChatId = 2, UserId = 10, NotificationsEnabled = false, JoinedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _service.GetAllChatSettingsAsync(10);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }
}