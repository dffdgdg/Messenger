using API.Application.Services.Abstractions;

namespace API.Application.Mapping;

internal static class AvatarUrlHelper
{
    public static string? BuildUserAvatarUrl(IUrlBuilder? urlBuilder, int userId, string? avatarPath)
    {
        if (string.IsNullOrEmpty(avatarPath)) return null;
        var version = Math.Abs(avatarPath.GetHashCode());
        return urlBuilder?.BuildUrl($"api/files/avatar/user/{userId}/download?v={version}");
    }

    public static string? BuildChatAvatarUrl(IUrlBuilder? urlBuilder, int chatId, string? avatarPath)
    {
        if (string.IsNullOrEmpty(avatarPath)) return null;
        var version = Math.Abs(avatarPath.GetHashCode());
        return urlBuilder?.BuildUrl($"api/files/avatar/chat/{chatId}/download?v={version}");
    }
}