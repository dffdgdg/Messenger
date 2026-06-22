using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class MessageMappingsTests
{
    private readonly Mock<IUrlBuilder> _urlMock = new();

    public MessageMappingsTests()
    {
        _urlMock.Setup(u => u.BuildUrl(It.IsAny<string>())).Returns<string?>(s => s);
    }

    [Fact]
    public void ToDto_UserMessage_BasicMapping()
    {
        var msg = new UserMessage
        {
            Id = 1,
            ChatId = 10,
            SenderId = 100,
            Content = "Hello",
            CreatedAt = new DateTime(2025, 1, 1),
            Sender = new User { Surname = "Smith", Name = "John" }
        };

        var dto = msg.ToDto(100, _urlMock.Object);

        dto.Id.Should().Be(1);
        dto.ChatId.Should().Be(10);
        dto.Content.Should().Be("Hello");
        dto.IsOwn.Should().BeTrue();
        dto.IsSystemMessage.Should().BeFalse();
        dto.SenderName.Should().Be("Smith John");
    }

    [Fact]
    public void ToDto_DeletedMessage_ShowsPlaceholder()
    {
        var msg = new UserMessage
        {
            Id = 1,
            ChatId = 10,
            Content = "Secret",
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow
        };

        var dto = msg.ToDto();

        dto.Content.Should().Be("[Сообщение удалено]");
        dto.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void ToDto_SystemMessage_MapsCorrectly()
    {
        var msg = new SystemMessage
        {
            Id = 1,
            ChatId = 10,
            InitiatorId = 100,
            SystemEventType = SystemEventType.ChatCreated,
            CreatedAt = new DateTime(2025, 1, 1),
            Initiator = new User { Surname = "Admin", Name = "System" }
        };

        var dto = msg.ToDto();

        dto.IsSystemMessage.Should().BeTrue();
        dto.SystemEventType.Should().Be(SystemEventType.ChatCreated);
        dto.SenderName.Should().Be("Admin System");
    }

    [Fact]
    public void ToDto_PinnedMessage_HasPinnedFlags()
    {
        var pinnedAt = new DateTime(2025, 6, 1);
        var msg = new UserMessage
        {
            Id = 1,
            ChatId = 10,
            PinnedAt = pinnedAt,
            PinnedByUserId = 5,
            CreatedAt = DateTime.UtcNow
        };

        var dto = msg.ToDto();

        dto.IsPinned.Should().BeTrue();
        dto.PinnedAt.Should().Be(pinnedAt);
        dto.PinnedByUserId.Should().Be(5);
    }

    [Fact]
    public void ToReplyPreviewDto_UserMessage_ReturnsPreview()
    {
        var msg = new UserMessage
        {
            Id = 1,
            ChatId = 10,
            SenderId = 100,
            Content = "Reply text",
            CreatedAt = DateTime.UtcNow
        };

        var preview = msg.ToReplyPreviewDto();

        preview.Id.Should().Be(1);
        preview.Content.Should().Be("Reply text");
        preview.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void ToReplyPreviewDto_DeletedMessage_ReturnsNullContent()
    {
        var msg = new UserMessage
        {
            Id = 1,
            ChatId = 10,
            Content = "Deleted reply",
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow
        };

        var preview = msg.ToReplyPreviewDto();

        preview.Content.Should().BeNull();
        preview.IsDeleted.Should().BeTrue();
    }
}