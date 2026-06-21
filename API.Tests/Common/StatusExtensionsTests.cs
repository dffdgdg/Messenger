using API.Common.Http;
using FluentAssertions;
using Xunit;

namespace API.Tests.Common;

public class StatusExtensionsTests
{
    [Theory]
    [InlineData("15m", 15)]
    [InlineData("30m", 30)]
    [InlineData("1h", 60)]
    [InlineData("2h", 120)]
    [InlineData("4h", 240)]
    [InlineData("8h", 480)]
    [InlineData("24h", 1440)]
    public void Parse_Valid_ReturnsCorrectTimeSpan(string duration, int expectedMinutes)
    {
        var result = duration.Parse();
        result.Should().NotBeNull();
        result!.Value.TotalMinutes.Should().Be(expectedMinutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("5m")]
    [InlineData("1d")]
    public void Parse_Invalid_ReturnsNull(string? duration)
    {
        var result = duration.Parse();
        result.Should().BeNull();
    }
}