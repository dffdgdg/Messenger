using FluentAssertions;
using Xunit;

namespace API.Tests.Services;

public class ValidationHelperTests
{
    [Theory]
    [InlineData("abc", true)]
    [InlineData("user_123", true)]
    [InlineData("a_very_long_username12", true)]
    [InlineData("ab", false)]
    [InlineData("", false)]
    [InlineData("User", false)]
    [InlineData("user name", false)]
    [InlineData("user@name", false)]
    [InlineData("u123456789012345678901234567890", false)]
    public void ValidateUsername_ReturnsExpectedResult(string username, bool expected)
    {
        var result = ValidationHelper.ValidateUsername(username);
        result.IsSuccess.Should().Be(expected, because: $"username '{username}' должен быть {(expected ? "валидным" : "невалидным")}");
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("longpass", true)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidatePassword_ReturnsExpectedResult(string? password, bool expected)
    {
        var result = ValidationHelper.ValidatePassword(password);

        result.IsSuccess.Should().Be(expected, because: $"пароль '{password}' должен быть {(expected ? "валидным" : "невалидным")}");
    }
}