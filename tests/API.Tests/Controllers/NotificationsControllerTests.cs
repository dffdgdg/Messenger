using API.Application.Common;
using API.Application.Features.Notification;
using API.Application.Features.Notification.Commands;
using API.Application.Features.Notification.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Chat;
using Xunit;

namespace API.Tests.Controllers;

public class NotificationsControllerTests : ControllerTestBase
{
    private readonly Mock<INotificationHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<SetChatMuteCommand, ChatNotificationSettingsDto>> _setChatMute = new();
    private readonly Mock<IQueryHandler<GetChatNotificationSettingsQuery, Result<ChatNotificationSettingsDto>>> _getChatSettings = new();
    private readonly Mock<IQueryHandler<GetAllChatSettingsQuery, Result<List<ChatNotificationSettingsDto>>>> _getAllSettings = new();

    private readonly NotificationsController _controller;

    public NotificationsControllerTests()
    {
        _handlers.Setup(h => h.SetChatMute).Returns(_setChatMute.Object);
        _handlers.Setup(h => h.GetChatSettings).Returns(_getChatSettings.Object);
        _handlers.Setup(h => h.GetAllSettings).Returns(_getAllSettings.Object);

        _controller = new NotificationsController(_handlers.Object, NullLogger<NotificationsController>.Instance);
        SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task GetChatSettings_Success_Returns200()
    {
        _getChatSettings
            .Setup(h => h.HandleAsync(It.IsAny<GetChatNotificationSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto()));

        var result = await _controller.GetChatSettings(10);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetChatSettings_Failure_Returns400()
    {
        _getChatSettings
            .Setup(h => h.HandleAsync(It.IsAny<GetChatNotificationSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatNotificationSettingsDto>.Failure("Чат не найден"));

        var result = await _controller.GetChatSettings(10);

        result.ShouldHaveStatus(400);
    }

    [Fact]
    public async Task SetChatMute_Success_Returns200()
    {
        _setChatMute
            .Setup(h => h.HandleAsync(It.IsAny<SetChatMuteCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatNotificationSettingsDto>.Success(new ChatNotificationSettingsDto()));

        var result = await _controller.SetChatMute(new ChatNotificationSettingsDto { ChatId = 10 });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetAllSettings_Success_Returns200()
    {
        _getAllSettings
            .Setup(h => h.HandleAsync(It.IsAny<GetAllChatSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChatNotificationSettingsDto>>.Success([]));

        var result = await _controller.GetAllSettings();

        result.ShouldHaveStatus(200);
    }
}