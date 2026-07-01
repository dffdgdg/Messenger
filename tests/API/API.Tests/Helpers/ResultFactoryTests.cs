using API.Tests.Infrastructure.Helpers;
using FluentAssertions;
using Xunit;

namespace API.Tests.Helpers;

public class ResultFactoryTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public void CreateByStatusCode_Typed_ReturnsCorrectErrorType(int statusCode)
    {
        var result = ResultFactory.CreateByStatusCode<string>(statusCode, "test error");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("test error");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public void CreateByStatusCode_Untyped_ReturnsCorrectErrorType(int statusCode)
    {
        var result = ResultFactory.CreateByStatusCode(statusCode, "test error");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("test error");
    }

    [Fact]
    public void CreateByStatusCode_InvalidCode_Throws()
    {
        Action act = () => ResultFactory.CreateByStatusCode<string>(200, "error");
        act.Should().Throw<ArgumentException>();
    }
}