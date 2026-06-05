using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests.Helpers;

internal static class ResultAssertionExtensions
{
    internal static ObjectResult ShouldHaveStatus(this IActionResult result, int statusCode)
    {
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(statusCode);
        return objectResult;
    }

    internal static TBody ShouldHaveBody<TBody>(this ObjectResult result)
    {
        return result.Value.Should().BeOfType<TBody>().Subject;
    }
}