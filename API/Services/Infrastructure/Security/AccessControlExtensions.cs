namespace API.Services.Infrastructure.Security;

public static class AccessControlExtensions
{
    public static async Task<Result> EnsureMemberOfAsync(this IAccessControlService access, int userId, int chatId)
        => await access.IsMemberAsync(userId, chatId) ? Result.Success() : Result.Forbidden("У вас нет доступа к этому чату");

    public static async Task<Result> EnsureAdminOfAsync(this IAccessControlService access, int userId, int chatId)
        => await access.IsAdminAsync(userId, chatId) ? Result.Success() : Result.Forbidden("Требуются права администратора");

    public static async Task<Result> EnsureOwnerOfAsync(this IAccessControlService access, int userId, int chatId)
        => await access.IsOwnerAsync(userId, chatId) ? Result.Success() : Result.Forbidden("Только владелец может выполнить это действие");
}