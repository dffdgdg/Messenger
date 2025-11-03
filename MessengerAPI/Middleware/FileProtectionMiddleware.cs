using MessengerAPI.Helpers;

namespace MessengerAPI.Middleware
{
    public class FileProtectionMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            if (IsProtectedPath(context.Request.Path))
            {
                string? token = null;
                string? authHeader = context.Request.Headers.Authorization;
                
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader["Bearer ".Length..].Trim();
                }

                if (string.IsNullOrEmpty(token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Требуется авторизация" });
                    return;
                }

                if (!TokenHelper.ValidateToken(token, out int userId))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Недействительный токен" });
                    return;
                }

                context.Items["UserId"] = userId;

                if (!await ValidateResourceAccess(context, userId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = "Доступ запрещен" });
                    return;
                }
            }

            await next(context);
        }

        private static bool IsProtectedPath(PathString path)
        {
            var protectedPaths = new[]
            {
                "/avatars",
                "/files",
                "/uploads"
            };

            return protectedPaths.Any(p => path.StartsWithSegments(p, System.StringComparison.OrdinalIgnoreCase));
        }

        private static Task<bool> ValidateResourceAccess(HttpContext context, int userId)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentOutOfRangeException.ThrowIfNegative(userId);
            // Здесь можно добавить дополнительную логику проверки прав доступа
            // Например, проверить принадлежит ли файл пользователю
            // или имеет ли пользователь необходимую роль

            // По умолчанию разрешаем доступ авторизованным пользователям
            return Task.FromResult(true);
        }
    }

    // Расширение для регистрации middleware
    public static class FileProtectionMiddlewareExtensions
    {
        public static IApplicationBuilder UseFileProtection(this IApplicationBuilder builder)=>
            builder.UseMiddleware<FileProtectionMiddleware>();
    }
}