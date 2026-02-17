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
    /// <summary>
    /// Обработчик нового сообщения из SignalR.
    /// Добавляет в коллекцию и, если нужно, запускает polling транскрипции.
    /// </summary>
    private void OnMessageReceived(MessageDTO messageDto) =>
        Dispatcher.UIThread.Post(async () =>
        {
            _messageManager.AddReceivedMessage(messageDto);
            UpdatePollsCount();

            // Запуск polling транскрипции для голосовых сообщений
            var addedMsg = Messages.LastOrDefault(m => m.Id == messageDto.Id);
            if (addedMsg != null)
                StartTranscriptionPollingIfNeeded(addedMsg);

            if (!IsScrolledToBottom)
                HasNewMessages = true;
            else
                await MarkMessagesAsReadAsync();
        });

    /// <summary>
    /// Обработчик события «сообщение прочитано» из SignalR.
    /// Обновляет статус доставки для исходящих сообщений.
    /// </summary>
    private void OnMessageRead(int chatId, int userId, int? lastReadMessageId, DateTime? readAt) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!lastReadMessageId.HasValue) return;

            foreach (var msg in Messages
                         .Where(m => m.Id <= lastReadMessageId.Value && m.SenderId == UserId))
            {
                msg.IsRead = true;
            }
        });

    /// <summary>
    /// Вызывается View при появлении сообщения в видимой области.
    /// Отмечает сообщение как прочитанное (однократно).
    /// </summary>
    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (!message.IsUnread || message.SenderId == UserId)
            return;

        message.IsUnread = false;
        _messageManager.MarkAsReadLocally(message.Id);

        if (_hubConnection != null)
            await _hubConnection.MarkMessageAsReadAsync(message.Id);
    }

    /// <summary>
    /// Отправляет серверу отметку о прочтении.
    /// Защищено cooldown'ом (<see cref="AppConstants.MarkAsReadCooldownSeconds"/>),
    /// чтобы не спамить при быстром скролле.
    /// </summary>
    public async Task MarkMessagesAsReadAsync(int? messageId = null)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);

        if (_hubConnection is not null)
            await _hubConnection.MarkAsReadAsync(messageId);
    }

    /// <summary>Упрощённый вызов для View — отметить видимые сообщения как прочитанные.</summary>
    public async Task OnMessagesVisibleAsync() => await MarkMessagesAsReadAsync();

    /// <summary>Подгрузка старых сообщений при скролле вверх.</summary>
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

    /// <summary>Подгрузка новых сообщений при скролле вниз (при наличии пропуска).</summary>
    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (_messageManager.IsLoading || !_messageManager.HasMoreNewer) return;

        var ct = _loadingCts?.Token ?? CancellationToken.None;
        await _messageManager.LoadNewerMessagesAsync(ct);
        UpdatePollsCount();
    }

    /// <summary>
    /// Отправка нового сообщения.
    /// Если активен режим редактирования — сохраняет редактирование.
    /// Поддерживает вложения и ответы.
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        // В режиме редактирования кнопка «Отправить» = «Сохранить»
        if (IsEditMode)
        {
            await SaveEditMessage();
            return;
        }

        // Не отправляем пустые сообщения без вложений
        if (string.IsNullOrWhiteSpace(NewMessage) && LocalAttachments.Count == 0)
            return;

        await SafeExecuteAsync(async ct =>
        {
            // Загружаем вложения на сервер
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

    /// <summary>Скролл к последним сообщениям и сброс счётчика непрочитанных.</summary>
    [RelayCommand]
    private async Task ScrollToLatest()
    {
        HasNewMessages = false;
        IsScrolledToBottom = true;
        await MarkMessagesAsReadAsync();
    }

    /// <summary>Пересчитывает количество опросов среди загруженных сообщений.</summary>
    private void UpdatePollsCount() => PollsCount = _messageManager.GetPollsCount();
}