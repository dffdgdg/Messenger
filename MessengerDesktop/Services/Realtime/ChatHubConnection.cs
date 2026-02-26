using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Auth;
using MessengerShared.Dto.Message;
using MessengerShared.Dto.ReadReceipt;
using MessengerShared.Dto.User;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Realtime;

public sealed class ChatHubConnection(int chatId, IAuthManager authManager) : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private bool _disposed;
    private int _lastSentReadMessageId;
    private DateTime _lastSentReadTime = DateTime.MinValue;
    private DateTime _lastSentTypingTime = DateTime.MinValue;

    public event Action<MessageDto>? MessageReceived;
    public event Action<MessageDto>? MessageUpdated;
    public event Action<int>? MessageDeleted;
    public event Action<int, int, int?, DateTime?>? MessageRead;
    public event Action<int, int>? UnreadCountUpdated;
    public event Action<int, int>? UserTyping;
    public event Action? Reconnected;

    /// <summary>Новый участник присоединился к чату.</summary>
    public event Action<int, UserDto>? MemberJoined;

    /// <summary>Участник покинул чат.</summary>
    public event Action<int, int>? MemberLeft;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{App.ApiUrl}chatHub", options =>
                    options.AccessTokenProvider = () => Task.FromResult(authManager.Session.Token))
                .WithAutomaticReconnect()
                .Build();

            //Сообщения
            _hubConnection.On<MessageDto>("ReceiveMessageDto", OnMessageReceived);
            _hubConnection.On<MessageDto>("MessageUpdated", OnMessageUpdated);
            _hubConnection.On<MessageDeletedEvent>("MessageDeleted", OnMessageDeletedEvent);
            _hubConnection.On<int, int, int?, DateTime?>("MessageRead", OnMessageRead);
            _hubConnection.On<int, int>("UnreadCountUpdated", OnUnreadCountUpdated);
            _hubConnection.On<int, int>("UserTyping", OnUserTyping);

            //Участники чата
            _hubConnection.On<int, UserDto>("MemberJoined", OnMemberJoined);
            _hubConnection.On<int, int>("MemberLeft", OnMemberLeft);

            _hubConnection.Reconnected += OnReconnected;

            await _hubConnection.StartAsync(ct);
            await _hubConnection.InvokeAsync("JoinChat", chatId, ct);

            Debug.WriteLine($"[ChatHub] Connected to chat {chatId}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] Connection error: {ex.Message}");
        }
    }

    private async Task OnReconnected(string? _)
    {
        Debug.WriteLine($"[ChatHub] Reconnected, rejoining chat {chatId}");
        await _hubConnection!.InvokeAsync("JoinChat", chatId);
        Reconnected?.Invoke();
    }

    public async Task<ChatReadInfoDto?> GetReadInfoAsync()
    {
        if (!IsConnected) return null;

        try
        {
            return await _hubConnection!.InvokeAsync<ChatReadInfoDto?>("GetReadInfo", chatId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] GetReadInfo error: {ex.Message}");
            return null;
        }
    }

    public async Task MarkAsReadAsync(int? messageId = null)
    {
        if (!IsConnected) return;

        try
        {
            await _hubConnection!.InvokeAsync("MarkAsRead", chatId, messageId);
            Debug.WriteLine($"[ChatHub] Marked as read: chat={chatId}, messageId={messageId?.ToString() ?? "latest"}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] MarkAsRead error: {ex.Message}");
        }
    }

    public async Task MarkMessageAsReadAsync(int messageId)
    {
        if (!IsConnected || messageId <= _lastSentReadMessageId) return;

        var now = DateTime.UtcNow;
        if ((now - _lastSentReadTime).TotalMilliseconds < AppConstants.MarkAsReadDebounceMs)
            return;

        _lastSentReadMessageId = messageId;
        _lastSentReadTime = now;

        try
        {
            await _hubConnection!.InvokeAsync("MarkMessageAsRead", chatId, messageId);
            Debug.WriteLine($"[ChatHub] Message {messageId} marked as read");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] MarkMessageAsRead error: {ex.Message}");
        }
    }

    public async Task<AllUnreadCountsDto?> GetUnreadCountsAsync()
    {
        if (!IsConnected) return null;

        try
        {
            return await _hubConnection!.InvokeAsync<AllUnreadCountsDto>("GetUnreadCounts");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] GetUnreadCounts error: {ex.Message}");
            return null;
        }
    }
    public async Task SendTypingAsync()
    {
        if (!IsConnected) return;

        var now = DateTime.UtcNow;
        if ((now - _lastSentTypingTime).TotalMilliseconds < AppConstants.TypingSendDebounceMs)
            return;

        _lastSentTypingTime = now;

        try
        {
            await _hubConnection!.InvokeAsync("SendTyping", chatId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] SendTyping error: {ex.Message}");
        }
    }
    #region Event Handlers

    private void OnMessageReceived(MessageDto message)
    {
        if (message.ChatId == chatId)
            MessageReceived?.Invoke(message);
    }

    private void OnMessageUpdated(MessageDto message)
    {
        if (message.ChatId == chatId)
            MessageUpdated?.Invoke(message);
    }

    private void OnMessageDeletedEvent(MessageDeletedEvent evt)
    {
        if (evt.ChatId == chatId)
            MessageDeleted?.Invoke(evt.MessageId);
    }

    private void OnMessageRead(int cId, int userId, int? lastReadMessageId, DateTime? readAt)
    {
        if (cId == chatId)
            MessageRead?.Invoke(cId, userId, lastReadMessageId, readAt);
    }

    private void OnUnreadCountUpdated(int cId, int unreadCount)
    {
        if (cId == chatId)
            UnreadCountUpdated?.Invoke(cId, unreadCount);
    }

    private void OnUserTyping(int cId, int userId)
    {
        if (cId == chatId)
            UserTyping?.Invoke(cId, userId);
    }
        
    private void OnMemberJoined(int cId, UserDto user)
    {
        if (cId == chatId)
            MemberJoined?.Invoke(cId, user);
    }

    private void OnMemberLeft(int cId, int userId)
    {
        if (cId == chatId)
            MemberLeft?.Invoke(cId, userId);
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hubConnection is not null)
        {
            try
            {
                _hubConnection.Reconnected -= OnReconnected;
                await _hubConnection.InvokeAsync("LeaveChat", chatId);
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatHub] Error disposing: {ex.Message}");
            }
            _hubConnection = null;
        }
    }
}