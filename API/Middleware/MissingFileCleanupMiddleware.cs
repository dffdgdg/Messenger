using API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace API.Web.Middleware;

public sealed partial class MissingFileCleanupMiddleware(RequestDelegate next)
{
    private static readonly PathString[] WatchedPrefixes = [new("/uploads"), new("/avatars")];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsWatchedRequest(context.Request.Path))
        {
            await next(context);
            return;
        }

        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status404NotFound)
            return;

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            return;

        var relativePath = context.Request.Path.Value?.TrimStart('/');
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        var dbContext = context.RequestServices.GetRequiredService<MessengerDbContext>();
        var logger = context.RequestServices.GetRequiredService<ILogger<MissingFileCleanupMiddleware>>();

        var alternativePath = $"/{relativePath}";

        var hasReferences = await CheckAnyReferenceAsync(dbContext, relativePath, alternativePath, context.RequestAborted);
        if (!hasReferences)
            return;

        await CleanupReferencesAsync(dbContext, relativePath, alternativePath, context.RequestAborted);

        LogMissingFileCleaned(logger, relativePath);
    }

    private static bool IsWatchedRequest(PathString path)
        => WatchedPrefixes.Any(path.StartsWithSegments);

    private static async Task<bool> CheckAnyReferenceAsync(MessengerDbContext dbContext, string path, string altPath, CancellationToken ct) => await dbContext.MessageFiles.AnyAsync(f => f.Path == path || f.Path == altPath, ct)
            || await dbContext.Users.AnyAsync(u => u.Avatar == path || u.Avatar == altPath, ct)
            || await dbContext.Chats.AnyAsync(c => c.Avatar == path || c.Avatar == altPath, ct);

    private static async Task CleanupReferencesAsync(MessengerDbContext dbContext, string path, string altPath, CancellationToken ct)
    {
        await dbContext.MessageFiles.Where(f => f.Path == path || f.Path == altPath).ExecuteDeleteAsync(ct);

        await dbContext.Users.Where(u => u.Avatar == path || u.Avatar == altPath).ExecuteUpdateAsync(setter => setter.SetProperty(u => u.Avatar, (string?)null), ct);

        await dbContext.Chats.Where(c => c.Avatar == path || c.Avatar == altPath).ExecuteUpdateAsync(setter => setter.SetProperty(c => c.Avatar, (string?)null), ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ссылка на отсутствующий файл {Path} очищена из БД")]
    private static partial void LogMissingFileCleaned(ILogger logger, string path);
}
public static class MissingFileCleanupMiddlewareExtensions
{
    public static IApplicationBuilder UseMissingFileCleanup(this IApplicationBuilder app)
        => app.UseMiddleware<MissingFileCleanupMiddleware>();
}