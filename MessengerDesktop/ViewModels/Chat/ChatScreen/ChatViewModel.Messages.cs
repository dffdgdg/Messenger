using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerShared.DTO.Message;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    private void OnMessageReceived(MessageDTO messageDto) => Dispatcher.UIThread.Post(async () =>
    {
        _messageManager.AddReceivedMessage(messageDto);
        UpdatePollsCount();

        var addedMsg = Messages.LastOrDefault(m => m.Id == messageDto.Id);
        if (addedMsg != null)
        {
            StartTranscriptionPollingIfNeeded(addedMsg);
        }

        if (!IsScrolledToBottom)
            HasNewMessages = true;
        else await MarkMessagesAsReadAsync();
    });

    private void OnMessageRead(int chatId, int userId, int? lastReadMessageId, DateTime? readAt) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (lastReadMessageId.HasValue)
            {
                foreach (var msg in Messages.Where(m => m.Id <= lastReadMessageId.Value && m.SenderId == UserId))
                {
                    msg.IsRead = true;
                }
            }
        });

    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (!message.IsUnread || message.SenderId == UserId)
            return;

        message.IsUnread = false;
        _messageManager.MarkAsReadLocally(message.Id);

        if (_hubConnection != null)
        {
            await _hubConnection.MarkMessageAsReadAsync(message.Id);
        }
    }

    public async Task MarkMessagesAsReadAsync(int? messageId = null)
    {
        if ((DateTime.UtcNow - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        if (_hubConnection is not null)
        {
            await _hubConnection.MarkAsReadAsync(messageId);
        }
    }

    public async Task OnMessagesVisibleAsync() => await MarkMessagesAsReadAsync();

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (_messageManager.IsLoading) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;

        try
        {
            IsLoadingOlderMessages = true;
            await _messageManager.LoadOlderMessagesAsync(ct);
            UpdatePollsCount();
        }
        finally
        {
            IsLoadingOlderMessages = false;
        }
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (_messageManager.IsLoading || !_messageManager.HasMoreNewer) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;
        await _messageManager.LoadNewerMessagesAsync(ct);
        UpdatePollsCount();
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsEditMode)
        {
            await SaveEditMessage();
            return;
        }

        if (string.IsNullOrWhiteSpace(NewMessage) && LocalAttachments.Count == 0)
            return;

        await SafeExecuteAsync(async ct =>
        {
            var files = await _attachmentManager.UploadAllAsync(ct);

            var msg = new MessageDTO
            {
                ChatId = _chatId,
                Content = NewMessage,
                SenderId = UserId,
                Files = files,
                ReplyToMessageId = ReplyingToMessage?.Id
            };

            var result = await _apiClient.PostAsync<MessageDTO, MessageDTO>(
                ApiEndpoints.Message.Create, msg, ct);

            if (result.Success)
            {
                NewMessage = string.Empty;
                _attachmentManager.Clear();
                CancelReply();
            }
            else
            {
                ErrorMessage = $"Ошибка отправки: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task ScrollToLatest()
    {
        HasNewMessages = false;
        IsScrolledToBottom = true;
        await MarkMessagesAsReadAsync();
    }

    private void UpdatePollsCount() => PollsCount = _messageManager.GetPollsCount();
}