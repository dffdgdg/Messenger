namespace MessengerAPI.Middleware;

public sealed class MissingFileCleanupMiddleware(RequestDelegate next)
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
        var messageFiles = await dbContext.MessageFiles
            .Where(f => f.Path == relativePath || f.Path == alternativePath)
            .ToListAsync(context.RequestAborted);

        var usersWithAvatar = await dbContext.Users
            .Where(u => u.Avatar == relativePath || u.Avatar == alternativePath)
            .ToListAsync(context.RequestAborted);

        var chatsWithAvatar = await dbContext.Chats
            .Where(c => c.Avatar == relativePath || c.Avatar == alternativePath)
            .ToListAsync(context.RequestAborted);

        if (messageFiles.Count == 0 && usersWithAvatar.Count == 0 && chatsWithAvatar.Count == 0)
            return;

        dbContext.MessageFiles.RemoveRange(messageFiles);

        foreach (var user in usersWithAvatar)
            user.Avatar = null;

        foreach (var chat in chatsWithAvatar)
            chat.Avatar = null;

        await dbContext.SaveChangesAsync(context.RequestAborted);

        logger.LogInformation(
            "Ссылка на отсутствующий файл {Path} очищена из БД. Удалено вложений: {Files}, очищено аватаров пользователей: {Users}, чатов: {Chats}",
            relativePath,messageFiles.Count,usersWithAvatar.Count,chatsWithAvatar.Count);
    }

    private static bool IsWatchedRequest(PathString path)
        => WatchedPrefixes.Any(prefix => path.StartsWithSegments(prefix));
}

public static class MissingFileCleanupMiddlewareExtensions
{
    public static IApplicationBuilder UseMissingFileCleanup(this IApplicationBuilder app)
        => app.UseMiddleware<MissingFileCleanupMiddleware>();
}
