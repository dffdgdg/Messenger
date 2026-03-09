using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    private void OnMessageReceived(MessageDto messageDto) =>
        Dispatcher.UIThread.Post(async () =>
        {
            if (_disposed) return;

            _messageManager.AddReceivedMessage(messageDto);
            UpdatePollsCount();

            var addedMsg = Messages.LastOrDefault(
                m => m.Id == messageDto.Id);
            if (addedMsg != null)
                StartTranscriptionPollingIfNeeded(addedMsg);

            if (!IsScrolledToBottom)
                HasNewMessages = true;
            else
                await MarkMessagesAsReadAsync();
        });

    private void OnMessageUpdatedInChat(MessageDto messageDto) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _messageManager.HandleMessageUpdated(messageDto);
        });

    private void OnMessageDeletedInChat(int messageId) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _messageManager.HandleMessageDeleted(messageId);
        });

    private void OnMessageRead(int? lastReadMessageId) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (!lastReadMessageId.HasValue) return;

            foreach (var msg in Messages.Where(
                m => m.Id <= lastReadMessageId.Value
                     && m.SenderId == UserId))
            {
                msg.IsRead = true;
            }
        });

    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (_disposed) return;

        if (!message.IsUnread || message.SenderId == UserId)
            return;

        message.IsUnread = false;
        _messageManager.MarkAsReadLocally(message.Id);

        await _globalHub.MarkMessageAsReadAsync(_chatId, message.Id);
    }

    public async Task MarkMessagesAsReadAsync()
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime =
            DateTime.SpecifyKind(now, DateTimeKind.Unspecified);

        await _globalHub.MarkChatAsReadAsync(_chatId);
    }

    public async Task OnMessagesVisibleAsync() => await MarkMessagesAsReadAsync();

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (_disposed || _messageManager.IsLoading) return;

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
        if (_disposed || _messageManager.IsLoading || !_messageManager.HasMoreNewer)
            return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;
        await _messageManager.LoadNewerMessagesAsync(ct);
        UpdatePollsCount();
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (_disposed) return;

        if (IsEditMode)
        {
            await SaveEditMessage();
            return;
        }

        var forwarding = ForwardingMessage;
        var hasForward = forwarding != null;
        var hasText = !string.IsNullOrWhiteSpace(NewMessage);
        var hasAttachments = LocalAttachments.Count > 0;

        if (!hasText && !hasAttachments && !hasForward)
            return;

        await SafeExecuteAsync(async ct =>
        {
            var files = await _attachmentManager.UploadAllAsync(ct);

            var content = NewMessage;
            if (hasForward && string.IsNullOrWhiteSpace(content))
            {
                content = forwarding!.Content;
            }

            var msg = new MessageDto
            {
                ChatId = _chatId,
                Content = content,
                SenderId = UserId,
                Files = files,
                ReplyToMessageId = ReplyingToMessage?.Id,
                ForwardedFromMessageId = forwarding?.Id
            };

            if (hasForward && files.Count == 0
                && forwarding!.Files.Count > 0)
            {
                msg.Files = forwarding.Files;
            }

            var result = await _apiClient
                .PostAsync<MessageDto, MessageDto>(
                    ApiEndpoints.Messages.Create, msg, ct);

            if (result.Success)
            {
                NewMessage = string.Empty;
                _attachmentManager.Clear();
                CancelReply();
                CancelForward();
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