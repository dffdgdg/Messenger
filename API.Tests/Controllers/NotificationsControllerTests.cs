using API.Common.Patterns;
using API.Controllers;
using API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Xunit;

namespace API.Tests.Controllers;

public class NotificationsControllerTests
{
    private readonly Mock<INotificationService> _notifMock = new();
    private readonly NotificationsController _controller;

    public NotificationsControllerTests()
    {
        _controller = new NotificationsController(_notifMock.Object, NullLogger<NotificationsController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task GetChatSettings_Success_Returns200()
    {
        _notifMock.Setup(s => s.GetChatNotificationSettingsAsync(582, 10))
            .Returns(Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto()).AsTask());

        var result = await _controller.GetChatSettings(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetChatSettings_NotFound_Returns404()
    {
        _notifMock.Setup(s => s.GetChatNotificationSettingsAsync(582, 10))
            .Returns(Result<ChatNotificationSettingsDto>.NotFound("Чат не найден").AsTask());

        var result = await _controller.GetChatSettings(10);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task SetChatMute_Success_Returns200()
    {
        var dto = new ChatNotificationSettingsDto { ChatId = 10, NotificationsEnabled = false };

        _notifMock.Setup(s => s.SetChatMuteAsync(582, dto))
            .Returns(Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto()).AsTask());

        var result = await _controller.SetChatMute(dto);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task SetChatMute_Forbidden_Returns403()
    {
        var dto = new ChatNotificationSettingsDto { ChatId = 10 };

        _notifMock.Setup(s => s.SetChatMuteAsync(582, dto))
            .Returns(Result<ChatNotificationSettingsDto>.Forbidden("Нет доступа").AsTask());

        var result = await _controller.SetChatMute(dto);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GetAllSettings_Success_Returns200()
    {
        _notifMock.Setup(s => s.GetAllChatSettingsAsync(582))
            .Returns(Result<List<ChatNotificationSettingsDto>>.Success([]).AsTask());

        var result = await _controller.GetAllSettings();

        result.ShouldHaveStatus(200);
    }
}