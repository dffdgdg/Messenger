using API.Application.Services.Abstractions;
using API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace API.Infrastructure.Services.Features;

public sealed class UserInfoService(MessengerDbContext context) : IUserInfoService
{
    public async Task<UserDisplayInfo?> GetDisplayInfoAsync(int userId, CancellationToken ct = default)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Surname, u.Name, u.Midname, u.Username, u.Avatar }).FirstOrDefaultAsync(ct);

        if (user is null) return null;

        var displayName = BuildDisplayName(user.Surname, user.Name, user.Midname) ?? user.Username ?? $"User {userId}";

        return new UserDisplayInfo(displayName, user.Avatar);
    }

    public async Task<string> GetChatNameAsync(int chatId, CancellationToken ct = default)
        => await context.Chats.AsNoTracking().Where(c => c.Id == chatId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

    public async Task UpdateLastOnlineAsync(int userId, DateTime lastOnline, CancellationToken ct = default)
        => await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastOnline, lastOnline), ct);

    private static string? BuildDisplayName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));

        var result = string.Join(" ", parts);
        return string.IsNullOrEmpty(result) ? null : result;
    }
}