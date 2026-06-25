using API.Application.Bundles;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Chat;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Services.Features.Chat;

public sealed partial class ChatMemberService(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    ChatBundle chat,
    IOnlineUserService onlineUserService,
    IHubNotifier hubNotifier,
    ILogger<ChatMemberService> logger)
    : BaseService<ChatMemberService>(unitOfWork, logger), IChatMemberService
{
    private readonly IChatRepository _chatRepository = chatRepository;
    private readonly ICacheService _cache = chat.Cache.CacheService;
    private readonly IAccessControlService _accessControl = chat.Cache.AccessControl;
    private readonly ISystemMessageService _systemMessages = chat.SystemMessages;
    private readonly AppDateTime _appDateTime = chat.Time.AppDateTime;
    private readonly IHubNotifier _hubNotifier = hubNotifier;

    public async Task<Result<ChatMemberDto>> AddMemberAsync(int chatId, int userId, int addedByUserId, ChatRole role = ChatRole.Member)
    {
        var admin = await _accessControl.EnsureMemberOfAsync(addedByUserId, chatId);
        if (admin.IsFailure) return admin.As<ChatMemberDto>();

        var exists = await _chatRepository.IsMemberAsync(chatId, userId);

        if (exists)
            return Result<ChatMemberDto>.Conflict("Пользователь уже является участником чата");

        var member = new ChatMember
        {
            ChatId = chatId,
            UserId = userId,
            Role = role,
            JoinedAt = _appDateTime.UtcNow,
            NotificationsEnabled = true
        };

        _chatRepository.AddMember(member);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatMemberDto>();

        _cache.InvalidateUserChats(userId);
        _cache.InvalidateMembership(userId, chatId);

        var chatEntity = await _chatRepository.FindByIdAsync(chatId);
        if (chatEntity is not null)
        {
            var chatUpdate = new ChatUpdateEventDto
            {
                Id = chatEntity.Id,
                Name = chatEntity.Name,
                Type = chatEntity.Type,
                CreatedById = chatEntity.CreatedById ?? 0,
                Avatar = chatEntity.Avatar,
                ShowHistoryForNewMembers = chatEntity.ShowHistoryForNewMembers,
                CurrentUserRole = role
            };

            await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatUpdated, chatUpdate);
        }

        await _hubNotifier.AddUserToChatGroupAsync(userId, chatId);
        LogMemberAdded(userId, chatId, addedByUserId);

        await _systemMessages.CreateAsync(chatId, addedByUserId, SystemEventType.MemberAdded, userId);

        return Result<ChatMemberDto>.Success(MapToDto(member));
    }

    public async Task<Result> RemoveMemberAsync(int chatId, int userId, int removedByUserId)
    {
        if (userId != removedByUserId)
        {
            var admin = await _accessControl.EnsureAdminOfAsync(removedByUserId, chatId);
            if (admin.IsFailure) return admin;
        }

        var member = await _chatRepository.GetMemberAsync(chatId, userId);

        if (member is null)
            return Result.NotFound("Пользователь не является участником чата");

        if (member.Role == ChatRole.Owner)
            return Result.Forbidden("Невозможно удалить владельца чата");

        _chatRepository.RemoveMember(member);

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save;

        _cache.InvalidateUserChats(userId);
        _cache.InvalidateMembership(userId, chatId);

        LogMemberRemoved(userId, chatId, removedByUserId);

        await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatRemoved, chatId);

        if (userId == removedByUserId)
            await _systemMessages.CreateAsync(chatId, userId, SystemEventType.MemberLeft);
        else
            await _systemMessages.CreateAsync(chatId, removedByUserId, SystemEventType.MemberRemoved, userId);

        return Result.Success();
    }

    public async Task<Result<ChatMemberDto>> UpdateRoleAsync(int chatId, int userId, ChatRole newRole, int updatedByUserId)
    {
        var owner = await _accessControl.EnsureOwnerOfAsync(updatedByUserId, chatId);
        if (owner.IsFailure) return owner.As<ChatMemberDto>();

        var member = await _chatRepository.GetMemberAsync(chatId, userId);

        if (member is null)
            return Result<ChatMemberDto>.NotFound("Пользователь не является участником чата");

        if (member.Role == ChatRole.Owner)
            return Result<ChatMemberDto>.Forbidden("Невозможно изменить роль владельца");

        if (newRole == ChatRole.Owner)
            return Result<ChatMemberDto>.Failure("Используйте отдельный метод для передачи владения");

        member.Role = newRole;

        var save = await SaveChangesAsync();
        if (save.IsFailure) return save.As<ChatMemberDto>();

        _cache.InvalidateMembership(userId, chatId);
        LogRoleUpdated(userId, chatId, newRole);

        await _systemMessages.CreateAsync(chatId, updatedByUserId, SystemEventType.RoleChanged, userId);

        var chatEntity = await _chatRepository.FindByIdAsync(chatId);

        if (chatEntity is not null)
        {
            var roleEvent = new ChatUpdateEventDto
            {
                Id = chatEntity.Id,
                Name = chatEntity.Name,
                Type = chatEntity.Type,
                CreatedById = chatEntity.CreatedById ?? 0,
                Avatar = chatEntity.Avatar,
                ShowHistoryForNewMembers = chatEntity.ShowHistoryForNewMembers,
                CurrentUserRole = newRole
            };

            await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatUpdated, roleEvent);
        }

        return Result<ChatMemberDto>.Success(MapToDto(member));
    }

    public async Task<Result<List<ChatMemberDto>>> GetMembersAsync(int chatId, int userId)
    {
        var access = await _accessControl.EnsureMemberOfAsync(userId, chatId);
        if (access.IsFailure) return access.As<List<ChatMemberDto>>();

        var members = await _chatRepository.GetMembersWithUsersAsync(chatId);

        return Result<List<ChatMemberDto>>.Success(members.ConvertAll(m => new ChatMemberDto
        {
            ChatId = chatId,
            UserId = m.UserId,
            Role = m.Role,
            JoinedAt = default,
            NotificationsEnabled = true
        }));
    }

    public async Task<Result> LeaveAsync(int chatId, int userId)
        => await RemoveMemberAsync(chatId, userId, userId);

    private static ChatMemberDto MapToDto(ChatMember member) => new()
    {
        ChatId = member.ChatId,
        UserId = member.UserId,
        Role = member.Role,
        JoinedAt = member.JoinedAt,
        NotificationsEnabled = member.NotificationsEnabled
    };

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} добавлен в чат {ChatId} пользователем {AddedBy}")]
    private partial void LogMemberAdded(int userId, int chatId, int addedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} удалён из чата {ChatId} пользователем {RemovedBy}")]
    private partial void LogMemberRemoved(int userId, int chatId, int removedBy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Роль пользователя {UserId} в чате {ChatId} изменена на {Role}")]
    private partial void LogRoleUpdated(int userId, int chatId, ChatRole role);

    #endregion
}
