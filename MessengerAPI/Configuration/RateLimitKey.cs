using System.Security.Claims;

namespace MessengerAPI.Configuration;

public static class RateLimitKey
{
    internal static string GetIpPartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    internal static string GetUserOrIpPartitionKey(HttpContext context) =>
        context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
