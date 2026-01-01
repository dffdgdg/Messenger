using MessengerDesktop.Services.Auth;
using MessengerShared.DTO;
using MessengerShared.DTO.Message;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public sealed class ChatHubConnection(int chatId, IAuthManager authManager) : IAsyncDisposable
{
    private readonly int _chatId = chatId;
    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
    private HubConnection? _hubConnection;
    private bool _disposed;

    private int _lastSentReadMessageId;
    private DateTime _lastSentReadTime = DateTime.MinValue;

    public event Action<MessageDTO>? MessageReceived;
    public event Action<int, int, int?, DateTime?>? MessageRead;
    public event Action<int, int>? UnreadCountUpdated;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            _hubConnection = new HubConnectionBuilder().WithUrl($"{App.ApiUrl}chatHub", options
                => options.AccessTokenProvider = () => Task.FromResult(_authManager.Session.Token)).WithAutomaticReconnect().Build();

            _hubConnection.On<MessageDTO>("ReceiveMessageDTO", OnMessageReceived);
            _hubConnection.On<int, int, int?, DateTime?>("MessageRead", OnMessageRead);
            _hubConnection.On<int, int>("UnreadCountUpdated", OnUnreadCountUpdated);

            _hubConnection.Reconnected += async _ =>
            {
                Debug.WriteLine($"[ChatHub] Reconnected, rejoining chat {_chatId}");
                await _hubConnection.InvokeAsync("JoinChat", _chatId);
            };

            await _hubConnection.StartAsync(ct);
            await _hubConnection.InvokeAsync("JoinChat", _chatId, ct);

            Debug.WriteLine($"[ChatHub] Connected to chat {_chatId}");
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

    /// <summary>
    /// Получить информацию о прочтении (lastReadMessageId, firstUnreadMessageId)
    /// </summary>
    public async Task<ChatReadInfoDTO?> GetReadInfoAsync()
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return null;

        try
        {
            return await _hubConnection.InvokeAsync<ChatReadInfoDTO?>("GetReadInfo", _chatId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] GetReadInfo error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Отметить все до последнего сообщения как прочитанные
    /// </summary>
    public async Task MarkAsReadAsync(int? messageId = null)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return;

        try
        {
            await _hubConnection.InvokeAsync("MarkAsRead", _chatId, messageId);
            Debug.WriteLine($"[ChatHub] Marked as read: chat={_chatId}, messageId={messageId?.ToString() ?? "latest"}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] MarkAsRead error: {ex.Message}");
        }
    }

    /// <summary>
    /// Отметить конкретное сообщение как прочитанное (при скролле)
    /// С debounce чтобы не спамить сервер
    /// </summary>
    public async Task MarkMessageAsReadAsync(int messageId)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return;

        if (messageId <= _lastSentReadMessageId)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastSentReadTime).TotalMilliseconds < 300)
            return;

        _lastSentReadMessageId = messageId;
        _lastSentReadTime = now;

        try
        {
            await _hubConnection.InvokeAsync("MarkMessageAsRead", _chatId, messageId);
            Debug.WriteLine($"[ChatHub] Message {messageId} marked as read");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] MarkMessageAsRead error: {ex.Message}");
        }
    }

    public async Task<AllUnreadCountsDTO?> GetUnreadCountsAsync()
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
            return null;

        try
        {
            return await _hubConnection.InvokeAsync<AllUnreadCountsDTO>("GetUnreadCounts");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] GetUnreadCounts error: {ex.Message}");
            return null;
        }
    }

    private void OnMessageReceived(MessageDTO message)
    {
        if (message.ChatId == _chatId)
            MessageReceived?.Invoke(message);
    }

    private void OnMessageRead(int chatId, int userId, int? lastReadMessageId, DateTime? readAt)
    {
        if (chatId == _chatId)
            MessageRead?.Invoke(chatId, userId, lastReadMessageId, readAt);
    }

    private void OnUnreadCountUpdated(int chatId, int unreadCount)
    {
        if (chatId == _chatId)
            UnreadCountUpdated?.Invoke(chatId, unreadCount);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hubConnection is not null)
        {
            try
            {
                await _hubConnection.InvokeAsync("LeaveChat", _chatId);
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