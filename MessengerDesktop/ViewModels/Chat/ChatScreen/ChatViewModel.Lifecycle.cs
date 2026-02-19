using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Infrastructure.Helpers;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.Realtime;
using MessengerShared.DTO;
using MessengerShared.DTO.User;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    /// <summary>
    /// Главный метод инициализации. Вызывается из конструктора (fire-and-forget).
    /// Последовательно загружает: чат → участников → хаб → сообщения → настройки.
    /// После — определяет позицию скролла.
    /// </summary>
    private async Task InitializeChatAsync()
    {
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;

        try
        {
            IsInitialLoading = true;

            // 1. Загрузка метаданных чата
            await LoadChatAsync(ct);

            // 2. Загрузка участников (и контактного пользователя для 1-на-1)
            await LoadMembersAsync(ct);

            // 3. Подключение к SignalR-хабу чата
            await InitHubAsync(ct);

            // 4. Получение информации о прочитанных сообщениях
            var readInfo = await _hubConnection!.GetReadInfoAsync();
            _messageManager.SetReadInfo(readInfo);

            // 5. Загрузка начальной порции сообщений
            var scrollToIndex = await _messageManager.LoadInitialMessagesAsync(ct);

            // 6. Настройки уведомлений (не критично — ошибки глотаются)
            await LoadNotificationSettingsAsync(ct);

            // 7. Подсчёт опросов
            UpdatePollsCount();

            // 8. Инициализация голосовых сообщений
            var audioRecorder = App.Current.Services.GetRequiredService<IAudioRecorderService>();
            InitializeVoice(audioRecorder);

            // Инициализация завершена
            _initTcs.TrySetResult();

            // 9. Скролл к нужной позиции
            await Task.Delay(150, ct);

            if (scrollToIndex < Messages.Count - 1)
            {
                // Есть непрочитанные — скроллим к первому из них
                ScrollToIndexRequested?.Invoke(scrollToIndex.Value, false);
                Debug.WriteLine($"[ChatViewModel] Scrolling to first unread at index {scrollToIndex.Value}");
            }
            else
            {
                // Всё прочитано — скроллим вниз
                ScrollToBottomRequested?.Invoke();
                Debug.WriteLine("[ChatViewModel] Scrolling to bottom (no unread or at end)");
            }
        }
        catch (OperationCanceledException)
        {
            _initTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] Init error: {ex.Message}");
            ErrorMessage = $"Ошибка инициализации чата: {ex.Message}";
            _initTcs.TrySetException(ex);
        }
        finally
        {
            IsInitialLoading = false;
        }
    }

    /// <summary>Загружает метаданные чата (название, аватар, тип).</summary>
    private async Task LoadChatAsync(CancellationToken ct)
    {
        var result = await _apiClient.GetAsync<ChatDTO>(ApiEndpoints.Chat.ById(_chatId), ct);

        if (result.Success && result.Data is not null)
        {
            // Добавляем cache-buster к URL аватара
            if (!string.IsNullOrEmpty(result.Data.Avatar))
                result.Data.Avatar = AvatarHelper.GetUrlWithCacheBuster(result.Data.Avatar);

            Chat = result.Data;
        }
        else
        {
            throw new HttpRequestException($"Ошибка загрузки чата: {result.Error}");
        }
    }

    /// <summary>
    /// Загружает участников чата.
    /// Для контактных чатов дополнительно определяет собеседника.
    /// </summary>
    private async Task LoadMembersAsync(CancellationToken ct)
    {
        Members = await _memberLoader.LoadMembersAsync(Chat, ct);

        if (IsContactChat)
            LoadContactUser();

        OnPropertyChanged(nameof(InfoPanelSubtitle));
    }

    /// <summary>
    /// Определяет собеседника в контактном чате (1-на-1).
    /// Устанавливает аватар, имя и статус «последний раз в сети».
    /// </summary>
    private void LoadContactUser()
    {
        try
        {
            var contact = Members.FirstOrDefault(m => m.Id != UserId);
            if (contact == null) return;

            ContactUser = contact;
            IsContactOnline = contact.IsOnline;
            ContactLastSeen = FormatLastSeen(contact);

            // Переопределяем название и аватар чата данными собеседника
            if (Chat != null)
            {
                Chat.Name = contact.DisplayName ?? contact.Username ?? Chat.Name;

                if (!string.IsNullOrEmpty(contact.Avatar))
                    Chat.Avatar = contact.Avatar;
            }

            // Обновляем все зависимые свойства
            OnPropertyChanged(nameof(IsContactChat));
            OnPropertyChanged(nameof(IsGroupChat));
            OnPropertyChanged(nameof(InfoPanelTitle));
            OnPropertyChanged(nameof(InfoPanelSubtitle));
            OnPropertyChanged(nameof(ContactUser));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] LoadContactUser error: {ex.Message}");
        }
    }

    /// <summary>Форматирует строку «последний раз в сети» для собеседника.</summary>
    private static string? FormatLastSeen(UserDTO contact)
    {
        if (contact.IsOnline || !contact.LastOnline.HasValue)
            return null;

        var elapsed = DateTime.Now - contact.LastOnline.Value;

        return elapsed.TotalMinutes switch
        {
            < 1 => "был(а) только что",
            < 60 => $"был(а) {(int)elapsed.TotalMinutes} мин. назад",
            < 1440 => $"был(а) {(int)elapsed.TotalHours} ч. назад",
            < 2880 => "был(а) вчера",
            < 10080 => $"был(а) {(int)elapsed.TotalDays} дн. назад",
            _ => $"был(а) {contact.LastOnline.Value:dd.MM.yyyy}"
        };
    }

    /// <summary>Загружает настройки уведомлений. Ошибки не критичны.</summary>
    private async Task LoadNotificationSettingsAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _notificationApiService.GetChatSettingsAsync(_chatId, ct);
            if (settings != null)
                IsNotificationEnabled = settings.NotificationsEnabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] LoadNotificationSettings error: {ex.Message}");
        }
    }

    /// <summary>Создаёт и подключает SignalR-хаб для получения сообщений в реальном времени.</summary>
    private async Task InitHubAsync(CancellationToken ct)
    {
        _hubConnection = new ChatHubConnection(_chatId, _authManager);
        _hubConnection.MessageReceived += OnMessageReceived;
        _hubConnection.MessageUpdated += OnMessageUpdatedInChat;
        _hubConnection.MessageDeleted += OnMessageDeletedInChat;
        _hubConnection.MessageRead += OnMessageRead;
        _hubConnection.Reconnected += OnChatHubReconnected;
        await _hubConnection.ConnectAsync(ct);
    }

    private void OnChatHubReconnected()
    {
        Debug.WriteLine($"[ChatViewModel] Chat hub reconnected, triggering gap fill for chat {_chatId}");

        _ = Task.Run(async () =>
        {
            try
            {
                var ct = _loadingCts?.Token ?? CancellationToken.None;
                await _messageManager.GapFillAfterReconnectAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatViewModel] Gap fill error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Освобождение ресурсов: отключение от хаба, отмена загрузок,
    /// очистка коллекций, dispose менеджеров.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_globalHub is GlobalHubConnection hub)
            hub.SetCurrentChat(null);

        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = null;

        if (_hubConnection is not null)
        {
            _hubConnection.MessageReceived -= OnMessageReceived;
            _hubConnection.MessageUpdated -= OnMessageUpdatedInChat;
            _hubConnection.MessageDeleted -= OnMessageDeletedInChat;
            _hubConnection.MessageRead -= OnMessageRead;
            _hubConnection.Reconnected -= OnChatHubReconnected;
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        DisposeVoice();

        Messages.Clear();
        Members.Clear();
        LocalAttachments.Clear();
        _attachmentManager.Dispose();

        Dispose();
        GC.SuppressFinalize(this);
    }
}