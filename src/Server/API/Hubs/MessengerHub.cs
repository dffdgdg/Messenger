using API.Application.Features.Call;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Infrastructure.Services.Call;
using API.Infrastructure.Services.Features.Call;
using API.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Shared.Enum;
using Shared.HubProtocol;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace API.Web.Hubs;

[Authorize]
public sealed partial class MessengerHub(
    IAccessControlService accessControl,
    IOnlineUserService onlineUserService,
    ICallSessionService callSessions,
    ISystemMessageService systemMessages,
    IReadReceiptService readReceiptService,
    IUserStatusService userStatusService,
    IUserInfoService userInfoService,
    TurnCredentialService turnCredentialService,
    AppDateTime appDateTime,
    IConfiguration configuration,
    ILogger<MessengerHub> logger) : Hub
{
    // Кэш инфо о пользователях в рамках соединения с хабом.
    // Не является полноценным кэшем — живёт только пока открыто соединение.
    private readonly ConcurrentDictionary<int, Task<UserDisplayInfo>> _userInfoCache = new();

    #region Connection lifecycle

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            await base.OnConnectedAsync();
            return;
        }

        onlineUserService.UserConnected(userId.Value, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        var chatIds = await accessControl.GetUserChatIdsAsync(userId.Value);
        await Task.WhenAll(chatIds.Select(id => Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(id))));

        var statusResult = await userStatusService.GetStatusAsync(userId.Value);
        if (statusResult.TryUnwrap(out var statusDto, logger))
        {
            await Clients.Caller.SendAsync(HubMethods.Chat.UserStatusChanged, statusDto);
            await Clients.Others.SendAsync(HubMethods.Chat.UserStatusChanged, statusDto);
        }
        else
        {
            await Clients.Others.SendAsync(HubMethods.Chat.UserOnline, userId.Value);
        }

        LogUserConnected(userId.Value, chatIds.Count);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            await base.OnDisconnectedAsync(exception);
            return;
        }

        onlineUserService.UserDisconnected(userId.Value, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        if (!onlineUserService.IsOnline(userId.Value))
        {
            try
            {
                await userInfoService.UpdateLastOnlineAsync(userId.Value, appDateTime.UtcNow);
                await Clients.Others.SendAsync(HubMethods.Chat.UserOffline, userId.Value);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при обработке отключения {UserId}", userId.Value);
            }

            logger.LogInformation("Пользователь {UserId} отключился", userId.Value);
        }

        await HandleUserDisconnectedFromCallsAsync(userId.Value);
        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Chat groups

    public async Task JoinChat(int chatId)
    {
        var userId = GetRequiredUserId();
        var result = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (result.IsFailure) throw new HubException(result.Error);

        await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(chatId));
        logger.LogDebug("Пользователь {UserId} присоединился к чату {ChatId}", userId, chatId);
    }

    public async Task LeaveChat(int chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroup(chatId));
        logger.LogDebug("Соединение {ConnectionId} покинуло чат {ChatId}", Context.ConnectionId, chatId);
    }

    #endregion

    #region Typing and presence

    public async Task SendTyping(int chatId)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
            await Clients.OthersInGroup(ChatGroup(chatId)).SendAsync(HubMethods.Chat.UserTyping, chatId, userId.Value);
    }

    public async Task<List<int>> GetOnlineUsersInChat(int chatId)
    {
        var userId = GetRequiredUserId();
        var result = await accessControl.EnsureMemberOfAsync(userId, chatId);
        if (result.IsFailure) throw new HubException(result.Error);

        var memberIds = await accessControl.GetChatMemberIdsAsync(chatId);
        return [.. onlineUserService.FilterOnline(memberIds)];
    }

    public async Task SetStatus(int statusRaw, string? duration = null)
    {
        var userId = GetRequiredUserId();

        if (!Enum.IsDefined(typeof(UserStatusType), statusRaw))
        {
            logger.LogWarning("[SetStatus] Неверный статус={Status} от userId={UserId}", statusRaw, userId);
            throw new HubException($"Неверный статус: {statusRaw}");
        }

        var result = await userStatusService.SetStatusAsync(userId, (UserStatusType)statusRaw, duration.Parse());

        if (result.IsFailure)
            throw new HubException(result.Error);
    }

    #endregion

    #region User info cache

    private async Task<UserDisplayInfo> GetUserInfoAsync(int userId)
    {
        var task = _userInfoCache.GetOrAdd(userId, id => FetchAndCacheUserInfoAsync(id));

        try
        {
            return await task;
        }
        catch
        {
            _userInfoCache.TryRemove(userId, out _);
            return UserDisplayInfo.Unknown(userId);
        }
    }

    private async Task<UserDisplayInfo> FetchAndCacheUserInfoAsync(int userId)
    {
        var info = await userInfoService.GetDisplayInfoAsync(userId);
        return info ?? UserDisplayInfo.Unknown(userId);
    }

    #endregion

    #region Identity

    private int CurrentUserId
    {
        get
        {
            var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(raw, out var userId)) return userId;

            logger.LogError("[MessengerHub] Не удалось получить UserId. ConnectionId={ConnectionId}", Context.ConnectionId);
            throw new HubException("Невозможно идентифицировать пользователя.");
        }
    }

    private int? GetCurrentUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    private int GetRequiredUserId()
        => GetCurrentUserId() ?? throw new HubException("Пользователь не аутентифицирован");

    private static string UserGroup(int userId) => $"user_{userId}";
    private static string ChatGroup(int chatId) => $"chat_{chatId}";

    #endregion

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Пользователь {UserId} подключился, чатов: {ChatCount}")]
    private partial void LogUserConnected(int userId, int chatCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[MessengerHub] JoinCall: сессия {CallId} не найдена")]
    private partial void LogJoinCallSessionNotFound(string callId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[MessengerHub] JoinCall вернул false для {CallId}, userId={UserId}")]
    private partial void LogJoinCallReturnedFalse(string callId, int userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "[MessengerHub] JoinCall FAILED: callId={CallId}, userId={UserId}")]
    private partial void LogJoinCallFailed(string callId, int userId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "[MessengerHub] GetCallState failed для chatId={ChatId}")]
    private partial void LogGetCallStateFailed(int chatId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Звонок {CallId} в чате {ChatId} завершён по таймауту")]
    private partial void LogCallTimedOut(string callId, int chatId);

    #endregion
}