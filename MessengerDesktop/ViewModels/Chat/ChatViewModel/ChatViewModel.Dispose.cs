using MessengerDesktop.Services;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_globalHub is GlobalHubConnection hub)
        {
            hub.SetCurrentChat(null);
        }

        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = null;

        if (_hubConnection is not null)
        {
            _hubConnection.MessageReceived -= OnMessageReceived;
            _hubConnection.MessageRead -= OnMessageRead;
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        Messages.Clear();
        Members.Clear();
        LocalAttachments.Clear();
        _attachmentManager.Dispose();

        Dispose();
        GC.SuppressFinalize(this);
    }
}