using API.Application.Services.Abstractions;
using API.Domain.Repositories;
using Shared.Enum;

namespace API.Application.Features.Message.Queries;

internal static class MessageHistoryHelper
{
    public static async Task<DateTime?> GetHistoryCutoffAsync(int chatId, int userId, IChatRepository chatRepository, IAccessControlService accessControl)
    {
        var showHistory = await chatRepository.GetShowHistoryForNewMembersAsync(chatId);
        if (showHistory != false) return null;

        var role = await accessControl.GetRoleAsync(userId, chatId);
        if (role is ChatRole.Owner or ChatRole.Admin) return null;

        var member = await accessControl.GetChatMemberAsync(userId, chatId);
        return member?.JoinedAt ?? DateTime.MinValue;
    }
}
