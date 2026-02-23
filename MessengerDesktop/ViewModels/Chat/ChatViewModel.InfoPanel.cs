using Avalonia.Threading;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Realtime;
using MessengerShared.DTO;
using MessengerShared.DTO.User;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Partial: подписки и реактивное обновление ChatInfoPanel.
/// Гарантирует актуальность статуса, профиля контакта и списка участников
/// без ручного refresh.
/// </summary>
public partial class ChatViewModel
{
    /// <summary>
    /// Подписывается на события, влияющие на InfoPanel.
    /// Вызывается один раз из InitializeChatAsync после загрузки данных.
    /// </summary>
    private void SubscribeInfoPanelEvents()
    {
        // 1. Статус пользователей (online/offline)
        _globalHub.UserStatusChanged += OnUserStatusChangedForInfoPanel;

        // 2. Обновление профиля пользователя (аватар, имя, отдел)
        _globalHub.UserProfileUpdated += OnUserProfileUpdatedForInfoPanel;

        // 3. Изменение состава коллекции Members → обновляем subtitle
        Members.CollectionChanged += OnMembersCollectionChanged;

        // 4. Участники присоединились/покинули чат (от других пользователей)
        if (_hubConnection != null)
        {
            _hubConnection.MemberJoined += OnMemberJoined;
            _hubConnection.MemberLeft += OnMemberLeft;
        }
    }

    /// <summary>
    /// Отписывается от событий InfoPanel. Вызывается из DisposeAsync.
    /// </summary>
    private void UnsubscribeInfoPanelEvents()
    {
        _globalHub.UserStatusChanged -= OnUserStatusChangedForInfoPanel;
        _globalHub.UserProfileUpdated -= OnUserProfileUpdatedForInfoPanel;
        Members.CollectionChanged -= OnMembersCollectionChanged;

        if (_hubConnection != null)
        {
            _hubConnection.MemberJoined -= OnMemberJoined;
            _hubConnection.MemberLeft -= OnMemberLeft;
        }
    }

    /// <summary>
    /// Обработчик смены статуса пользователя (online/offline).
    /// Обновляет InfoPanel для 1:1 чатов и статус в списке участников для групп.
    /// </summary>
    private void OnUserStatusChangedForInfoPanel(int userId, bool isOnline)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Обновляем контакт в 1:1 чате
            if (IsContactChat && ContactUser != null && ContactUser.Id == userId)
            {
                IsContactOnline = isOnline;
                ContactUser.IsOnline = isOnline;

                if (isOnline)
                {
                    ContactLastSeen = null;
                    ContactUser.LastOnline = DateTime.UtcNow;
                }
                else
                {
                    ContactUser.LastOnline = DateTime.UtcNow;
                    ContactLastSeen = FormatLastSeen(ContactUser);
                }

                OnPropertyChanged(nameof(InfoPanelSubtitle));
                OnPropertyChanged(nameof(ContactLastSeen));
                OnPropertyChanged(nameof(IsContactOnline));
            }

            // Обновляем статус в списке участников (для групповых чатов)
            var member = Members.FirstOrDefault(m => m.Id == userId);
            if (member != null)
            {
                member.IsOnline = isOnline;
                if (!isOnline)
                    member.LastOnline = DateTime.UtcNow;

                // Принудительно обновляем элемент в коллекции,
                // чтобы ItemsControl перерисовал (UserDTO не INPC)
                var index = Members.IndexOf(member);
                if (index >= 0)
                {
                    Members[index] = member;
                }
            }
        });
    }

    /// <summary>
    /// Обработка обновления профиля пользователя (аватар, имя, отдел).
    /// </summary>
    private void OnUserProfileUpdatedForInfoPanel(UserDTO updatedUser) =>
        Dispatcher.UIThread.Post(() =>
        {
            // Обновляем контакт в 1:1 чате
            if (IsContactChat && ContactUser != null && ContactUser.Id == updatedUser.Id)
            {
                ContactUser = updatedUser;
                IsContactOnline = updatedUser.IsOnline;
                ContactLastSeen = FormatLastSeen(updatedUser);

                if (Chat != null)
                {
                    Chat.Name = updatedUser.DisplayName ?? updatedUser.Username ?? Chat.Name;
                    if (!string.IsNullOrEmpty(updatedUser.Avatar))
                        Chat.Avatar = updatedUser.Avatar;
                }

                InvalidateAllInfoPanelProperties();
            }

            // Обновляем участника в списке
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Id == updatedUser.Id)
                {
                    Members[i] = updatedUser;
                    break;
                }
            }
        });

    /// <summary>
    /// При изменении коллекции Members обновляем зависимые computed properties.
    /// </summary>
    private void OnMembersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InfoPanelSubtitle));
    }

    /// <summary>
    /// Новый участник присоединился к чату (событие от сервера).
    /// </summary>
    private void OnMemberJoined(int chatId, UserDTO user)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!Members.Any(m => m.Id == user.Id))
            {
                Members.Add(user);
                Debug.WriteLine($"[ChatViewModel] Member joined: {user.DisplayName} (chat {chatId})");
            }
        });
    }

    /// <summary>
    /// Участник покинул чат (событие от сервера).
    /// </summary>
    private void OnMemberLeft(int chatId, int userId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var member = Members.FirstOrDefault(m => m.Id == userId);
            if (member != null)
            {
                Members.Remove(member);
                Debug.WriteLine($"[ChatViewModel] Member left: {member.DisplayName} (chat {chatId})");
            }
        });
    }

    /// <summary>
    /// Перезагружает список участников после локального редактирования чата.
    /// Вызывается из callback'а OpenEditChat.
    /// </summary>
    private async Task ReloadMembersAfterEditAsync()
    {
        try
        {
            var ct = _loadingCts?.Token ?? CancellationToken.None;

            Debug.WriteLine($"[ChatViewModel] Reloading members after edit for chat {_chatId}");

            // Загружаем свежий список участников с сервера
            var freshMembers = await _memberLoader.LoadMembersAsync(Chat, ct);

            Dispatcher.UIThread.Post(() =>
            {
                // Заменяем коллекцию — OnMembersChanged переподпишет CollectionChanged
                Members = freshMembers;

                // Для 1:1 обновляем контакт
                if (IsContactChat)
                    LoadContactUser();

                InvalidateAllInfoPanelProperties();

                Debug.WriteLine($"[ChatViewModel] Members reloaded: {Members.Count} members");
            });
        }
        catch (OperationCanceledException)
        {
            // Нормально при dispose
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] ReloadMembersAfterEdit error: {ex.Message}");
        }
    }

    /// <summary>
    /// Перезагружает все данные InfoPanel: участников, статус, мета-данные чата.
    /// Вызывается при reconnect.
    /// </summary>
    private async Task RefreshInfoPanelDataAsync(CancellationToken ct = default)
    {
        try
        {
            Debug.WriteLine($"[ChatViewModel] Refreshing InfoPanel data for chat {_chatId}");

            // Перезагружаем метаданные чата
            await LoadChatAsync(ct);

            // Перезагружаем участников (OnMembersChanged переподпишет CollectionChanged)
            await LoadMembersAsync(ct);

            // Обновляем все computed properties
            Dispatcher.UIThread.Post(InvalidateAllInfoPanelProperties);

            Debug.WriteLine($"[ChatViewModel] InfoPanel data refreshed for chat {_chatId}");
        }
        catch (OperationCanceledException)
        {
            // Нормально при dispose
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] RefreshInfoPanelData error: {ex.Message}");
        }
    }

    /// <summary>
    /// Инвалидирует все computed properties, отображаемые в InfoPanel.
    /// </summary>
    private void InvalidateAllInfoPanelProperties()
    {
        OnPropertyChanged(nameof(IsContactChat));
        OnPropertyChanged(nameof(IsGroupChat));
        OnPropertyChanged(nameof(InfoPanelTitle));
        OnPropertyChanged(nameof(InfoPanelSubtitle));
        OnPropertyChanged(nameof(ContactAvatar));
        OnPropertyChanged(nameof(ContactDisplayName));
        OnPropertyChanged(nameof(ContactUsername));
        OnPropertyChanged(nameof(ContactDepartment));
        OnPropertyChanged(nameof(ContactLastSeen));
        OnPropertyChanged(nameof(IsContactOnline));
    }
}