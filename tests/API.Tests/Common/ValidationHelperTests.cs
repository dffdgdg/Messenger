using API.Application.Common;
using FluentAssertions;
using Xunit;

namespace API.Tests.Common;

public class ValidationHelperTests
{
    [Theory]
    [InlineData("john_doe")]
    [InlineData("a1b2c3")]
    [InlineData("test_user_123")]
    [InlineData("abc")]
    public void ValidateUsername_Valid_ReturnsSuccess(string username)
    {
        var result = ValidationHelper.ValidateUsername(username);
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("user@name")]
    [InlineData("UPPERCASE")]
    [InlineData("user name")]
    [InlineData("русский")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateUsername_Invalid_ReturnsFailure(string? username)
    {
        var result = ValidationHelper.ValidateUsername(username!);
        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("password123")]
    [InlineData("longenough")]
    public void ValidatePassword_Valid_ReturnsSuccess(string password)
    {
        var result = ValidationHelper.ValidatePassword(password);
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("short")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidatePassword_Invalid_ReturnsFailure(string? password)
    {
        var result = ValidationHelper.ValidatePassword(password!);
        result.IsFailure.Should().BeTrue();
    }
}