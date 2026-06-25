using API.Domain.Common;

namespace API.Application.Services.Abstractions;

public static class AccessControlExtensions
{
    public static async Task<Result> EnsureMemberOfAsync(this IAccessControlService accessControl, int userId, int chatId)
    {
        var isMember = await accessControl.IsMemberAsync(userId, chatId);
        return isMember ? Result.Success() : Result.Forbidden("Вы не являетесь участником этого чата");
    }

    public static async Task<Result> EnsureAdminOfAsync(this IAccessControlService accessControl, int userId, int chatId)
    {
        var isAdmin = await accessControl.IsAdminAsync(userId, chatId);
        return isAdmin ? Result.Success() : Result.Forbidden("Требуются права администратора");
    }

    public static async Task<Result> EnsureOwnerOfAsync(this IAccessControlService accessControl, int userId, int chatId)
    {
        var isOwner = await accessControl.IsOwnerAsync(userId, chatId);
        return isOwner ? Result.Success() : Result.Forbidden("Требуются права владельца");
    }
}
