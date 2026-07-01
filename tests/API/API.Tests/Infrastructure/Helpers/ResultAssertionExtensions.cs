using API.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Shared.Infrastructure;

namespace API.Tests.Infrastructure.Helpers;

internal static class ResultAssertionExtensions
{
    internal static ObjectResult ShouldHaveStatus(this IActionResult result, int statusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(statusCode);
        return objectResult;
    }

    internal static TBody ShouldHaveBody<TBody>(this ObjectResult result)
        => result.Value.Should().BeOfType<TBody>().Subject;

    internal static TData ShouldHaveSuccessBody<TData>(this IActionResult result)
    {
        var obj = result.ShouldHaveStatus(200);
        var apiResponse = obj.ShouldHaveBody<ApiResponse<TData>>();
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().NotBeNull();
        return apiResponse.Data!;
    }

    internal static void ShouldBe401(this IActionResult result)
        => result.Should().BeOfType<UnauthorizedObjectResult>().Which.StatusCode.Should().Be(401);

    internal static Task<Result<T>> AsTask<T>(this Result<T> result) => Task.FromResult(result);
    internal static Task<Result> AsTask(this Result result) => Task.FromResult(result);
}