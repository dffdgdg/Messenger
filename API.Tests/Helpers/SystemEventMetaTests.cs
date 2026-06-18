using FluentAssertions;
using Shared.Enum;
using Shared.Helpers;
using Xunit;

namespace API.Tests.Helpers;

public class SystemEventMetaTests
{
    [Theory]
    [InlineData(SystemEventType.ChatCreated, " создал(а) группу")]
    [InlineData(SystemEventType.MemberLeft, " покинул(а) группу")]
    [InlineData(SystemEventType.MessagePinned, " закрепил(а) сообщение")]
    public void GetPrefix_ReturnsCorrectPrefix(SystemEventType type, string expected)
    {
        var prefix = SystemEventMeta.GetPrefix(type);
        prefix.Should().Be(expected);
    }

    [Fact]
    public void GetPrefix_Null_ReturnsEmpty()
    {
        var prefix = SystemEventMeta.GetPrefix(null);
        prefix.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SystemEventType.MemberAdded, " в группу")]
    [InlineData(SystemEventType.MemberRemoved, " из группы")]
    public void GetSuffix_ReturnsCorrectSuffix(SystemEventType type, string expected)
    {
        var suffix = SystemEventMeta.GetSuffix(type);
        suffix.Should().Be(expected);
    }

    [Fact]
    public void Format_WithTarget_IncludesActorAndTarget()
    {
        var result = SystemEventMeta.Format(SystemEventType.MemberAdded, "Alice", "Bob");
        result.Should().Be("Alice добавил(а) Bob в группу");
    }

    [Fact]
    public void Format_WithoutTarget_IncludesActorOnly()
    {
        var result = SystemEventMeta.Format(SystemEventType.ChatCreated, "Charlie", "irrelevant");
        result.Should().Be("Charlie создал(а) группу");
    }

    [Fact]
    public void Format_NullType_ReturnsEmpty()
    {
        var result = SystemEventMeta.Format(null, "Actor", "Target");
        result.Should().BeEmpty();
    }
}