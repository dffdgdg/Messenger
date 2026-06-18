using API.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests;

public abstract class ControllerTestBase
{
    protected static void SetUser(ControllerBase controller, int userId, UserRole role = UserRole.User) =>
        AuthHelper.SetUser(controller, userId, role);

    protected static async Task AssertSuccess(Task<IActionResult> act)
    {
        var result = await act;
        result.ShouldHaveStatus(200);
    }

    protected static async Task AssertStatus(Task<IActionResult> act, int expectedStatus)
    {
        var result = await act;
        result.ShouldHaveStatus(expectedStatus);
    }

    protected static async Task<TData> AssertSuccessBody<TData>(Task<IActionResult> act)
    {
        var result = await act;
        return result.ShouldHaveSuccessBody<TData>();
    }
}