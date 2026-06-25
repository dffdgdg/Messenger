using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Options;
using Shared.Contracts.Chat;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Department;

public sealed class DepartmentSyncService(
    IDepartmentRepository departmentRepository,
    IChatRepository chatRepository,
    IHubNotifier hubNotifier,
    IOptions<MessengerSettings> settings,
    AppDateTime appDateTime)
{
    private readonly MessengerSettings _settings = settings.Value;

    /// <summary>
    /// Синхронизирует членство пользователя в чатах отделов.
    /// Возвращает SyncResult — что было добавлено/удалено.
    /// Не сохраняет изменения — вызывающий сам делает SaveChanges.
    /// </summary>
    public async Task<SyncResult> SyncDepartmentChatMembershipAsync(int userId, int? oldDeptId, int? newDeptId, CancellationToken ct)
    {
        if (oldDeptId == newDeptId)
            return SyncResult.Empty;

        var deptIds = new[] { oldDeptId, newDeptId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

        if (deptIds.Count == 0)
            return SyncResult.Empty;

        var chatMap = await departmentRepository.GetChatIdsForDepartmentsAsync(deptIds, ct);

        int? removedChatId = null;
        ChatUpdateEventDto? addedChat = null;

        if (oldDeptId.HasValue && chatMap.TryGetValue(oldDeptId.Value, out var oldChat))
        {
            removedChatId = oldChat.ChatId;
            var oldMember = await chatRepository.GetMemberAsync(oldChat.ChatId, userId, ct);
            if (oldMember is not null)
                chatRepository.RemoveMember(oldMember);
        }

        if (newDeptId.HasValue && chatMap.TryGetValue(newDeptId.Value, out var newChat))
        {
            var exists = await chatRepository.IsMemberAsync(newChat.ChatId, userId, ct);
            if (!exists)
            {
                chatRepository.AddMember(new ChatMember
                {
                    ChatId = newChat.ChatId,
                    UserId = userId,
                    Role = ChatRole.Member,
                    JoinedAt = appDateTime.UtcNow,
                    NotificationsEnabled = true
                });
            }

            addedChat = new ChatUpdateEventDto
            {
                Id = newChat.ChatId,
                Name = newChat.ChatName,
                Type = newChat.ChatType,
                CreatedById = newChat.CreatedById ?? 0,
                Avatar = newChat.Avatar,
                ShowHistoryForNewMembers = newChat.ShowHistoryForNewMembers,
                CurrentUserRole = ChatRole.Member
            };
        }

        return new SyncResult(removedChatId, addedChat);
    }

    public async Task NotifyMembershipChangedAsync(int userId,SyncResult sync, ICacheService cache,CancellationToken ct)
    {
        if (sync.RemovedChatId.HasValue)
        {
            cache.InvalidateMembership(userId, sync.RemovedChatId.Value);
            await hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatRemoved, sync.RemovedChatId.Value);
            await hubNotifier.RemoveUserFromChatGroupAsync(userId, sync.RemovedChatId.Value);
        }

        if (sync.AddedChat is not null)
        {
            cache.InvalidateMembership(userId, sync.AddedChat.Id);
            await hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatUpdated, sync.AddedChat);
            await hubNotifier.AddUserToChatGroupAsync(userId, sync.AddedChat.Id);
        }
    }

    public async Task SyncHeadsChatAsync(int? oldHeadId, int? newHeadId, CancellationToken ct, int? excludeDepartmentId = null)
    {
        if (oldHeadId == newHeadId) return;

        var setting = await departmentRepository.GetSystemSettingAsync("heads_chat_id", ct);
        if (setting is null || !int.TryParse(setting.Value, out var headsChatId))
            return;

        if (oldHeadId.HasValue)
        {
            var stillManages = excludeDepartmentId.HasValue
                ? await departmentRepository.IsHeadOfDepartmentAsync(oldHeadId.Value, excludeDepartmentId.Value, ct)
                : await departmentRepository.IsHeadOfAnyDepartmentAsync(oldHeadId.Value, ct);

            if (!stillManages)
            {
                var member = await chatRepository.GetMemberAsync(headsChatId, oldHeadId.Value, ct);
                if (member is not null)
                    chatRepository.RemoveMember(member);
            }
        }

        if (newHeadId.HasValue)
        {
            var exists = await chatRepository.IsMemberAsync(headsChatId, newHeadId.Value, ct);
            if (!exists)
            {
                chatRepository.AddMember(new ChatMember
                {
                    ChatId = headsChatId,
                    UserId = newHeadId.Value,
                    JoinedAt = appDateTime.UtcNow,
                    NotificationsEnabled = true
                });
            }
        }
    }

    public async Task NotifyUserRoleAsync(int userId, CancellationToken ct)
    {
        var role = await departmentRepository.ResolveUserRoleAsync(userId, _settings.AdminDepartmentId, ct);
        await hubNotifier.SendToUserAsync(userId, HubMethods.Chat.UserRoleUpdated, role);
    }

    public sealed record SyncResult(int? RemovedChatId, ChatUpdateEventDto? AddedChat)
    {
        public static SyncResult Empty { get; } = new(null, null);
    }
}
