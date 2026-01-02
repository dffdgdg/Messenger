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
            _hubConnection = new HubConnectionBuilder().WithUrl($"{App.ApiUrl}chatHub", options =>
            options.AccessTokenProvider = () => Task.FromResult(authManager.Session.Token)).WithAutomaticReconnect().Build();

            _hubConnection.On<MessageDTO>("ReceiveMessageDTO", OnMessageReceived);
            _hubConnection.On<int, int, int?, DateTime?>("MessageRead", OnMessageRead);
            _hubConnection.On<int, int>("UnreadCountUpdated", OnUnreadCountUpdated);

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
    }

    public async Task<ChatReadInfoDTO?> GetReadInfoAsync()
    {
        if (!IsConnected) return null;

        try
        {
            return await _hubConnection!.InvokeAsync<ChatReadInfoDTO?>("GetReadInfo", chatId);
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

    public async Task<AllUnreadCountsDTO?> GetUnreadCountsAsync()
    {
        if (!IsConnected) return null;

        try
        {
            return await _hubConnection!.InvokeAsync<AllUnreadCountsDTO>("GetUnreadCounts");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatHub] GetUnreadCounts error: {ex.Message}");
            return null;
        }
    }

    private void OnMessageReceived(MessageDTO message)
    {
        if (message.ChatId == chatId)
            MessageReceived?.Invoke(message);
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