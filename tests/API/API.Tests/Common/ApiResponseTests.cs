using FluentAssertions;
using Shared.Infrastructure;
using Xunit;

namespace API.Tests.Shared;

public class ApiResponseTests
{
    [Fact]
    public void Ok_SetsSuccessAndData()
    {
        var response = ApiResponse<string>.Ok("data", "message");

        response.Success.Should().BeTrue();
        response.Data.Should().Be("data");
        response.Message.Should().Be("message");
        response.Error.Should().BeNull();
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Fail_SetsErrorAndDetails()
    {
        var response = ApiResponse<int>.Fail("error", "details");

        response.Success.Should().BeFalse();
        response.Error.Should().Be("error");
        response.Details.Should().Be("details");
        response.Data.Should().Be(default);
        response.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ApiResponseHelper_Success_ReturnsCorrectApiResponse()
    {
        var response = ApiResponseHelper.Success(42, "ok");

        response.Success.Should().BeTrue();
        response.Data.Should().Be(42);
        response.Message.Should().Be("ok");
    }

    [Fact]
    public void ApiResponseHelper_Error_Typed_ReturnsCorrectApiResponse()
    {
        var response = ApiResponseHelper.Error<string>("fail", "det");

        response.Success.Should().BeFalse();
        response.Error.Should().Be("fail");
        response.Details.Should().Be("det");
    }

    [Fact]
    public void ApiResponseHelper_Error_Untyped_ReturnsCorrectApiResponse()
    {
        var response = ApiResponseHelper.Error("fail", "det");

        response.Success.Should().BeFalse();
        response.Error.Should().Be("fail");
        response.Details.Should().Be("det");
    }
}