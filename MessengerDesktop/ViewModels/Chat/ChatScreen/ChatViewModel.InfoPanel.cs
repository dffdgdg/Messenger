using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Partial: подписки и реактивное обновление ChatInfoPanel.
/// </summary>
public partial class ChatViewModel
{
    private void SubscribeInfoPanelEvents()
    {
        // Статус пользователей (online/offline)
        _globalHub.UserStatusChanged += OnUserStatusChangedForInfoPanel;

        // Обновление профиля пользователя (аватар, имя, отдел)
        _globalHub.UserProfileUpdated += OnUserProfileUpdatedForInfoPanel;

        // Изменение состава коллекции Members → обновляем subtitle
        Members.CollectionChanged += OnMembersCollectionChanged;

        // Участники присоединились/покинули чат — через GlobalHub
        _globalHub.MemberJoined += OnMemberJoinedForChat;
        _globalHub.MemberLeft += OnMemberLeftForChat;
    }

    private void UnsubscribeInfoPanelEvents()
    {
        _globalHub.UserStatusChanged -= OnUserStatusChangedForInfoPanel;
        _globalHub.UserProfileUpdated -= OnUserProfileUpdatedForInfoPanel;
        Members.CollectionChanged -= OnMembersCollectionChanged;

        _globalHub.MemberJoined -= OnMemberJoinedForChat;
        _globalHub.MemberLeft -= OnMemberLeftForChat;
    }

    /// <summary>Фильтр по chatId для MemberJoined.</summary>
    private void OnMemberJoinedForChat(int chatId, UserDto user)
    {
        if (chatId == _chatId)
            OnMemberJoined(chatId, user);
    }

    /// <summary>Фильтр по chatId для MemberLeft.</summary>
    private void OnMemberLeftForChat(int chatId, int userId)
    {
        if (chatId == _chatId)
            OnMemberLeft(chatId, userId);
    }

    private void OnUserStatusChangedForInfoPanel(int userId, bool isOnline)
    {
        Dispatcher.UIThread.Post(() =>
        {
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

            var member = Members.FirstOrDefault(m => m.Id == userId);
            if (member != null)
            {
                member.IsOnline = isOnline;
                if (!isOnline)
                    member.LastOnline = DateTime.UtcNow;

                var index = Members.IndexOf(member);
                if (index >= 0)
                {
                    Members[index] = member;
                }
            }
        });
    }

    private void OnUserProfileUpdatedForInfoPanel(UserDto updatedUser) =>
        Dispatcher.UIThread.Post(() =>
        {
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

            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Id == updatedUser.Id)
                {
                    Members[i] = updatedUser;
                    break;
                }
            }
        });

    private void OnMembersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(InfoPanelSubtitle));

    private void OnMemberJoined(int chatId, UserDto user)
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

    private async Task ReloadMembersAfterEditAsync()
    {
        try
        {
            var ct = _loadingCts?.Token ?? CancellationToken.None;

            Debug.WriteLine($"[ChatViewModel] Reloading members after edit for chat {_chatId}");

            var freshMembers = await _memberLoader.LoadMembersAsync(Chat, ct);

            Dispatcher.UIThread.Post(() =>
            {
                Members = freshMembers;

                if (IsContactChat)
                    LoadContactUser();

                InvalidateAllInfoPanelProperties();

                Debug.WriteLine($"[ChatViewModel] Members reloaded: {Members.Count} members");
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] ReloadMembersAfterEdit error: {ex.Message}");
        }
    }

    private async Task RefreshInfoPanelDataAsync(CancellationToken ct = default)
    {
        try
        {
            Debug.WriteLine($"[ChatViewModel] Refreshing InfoPanel data for chat {_chatId}");

            await LoadChatAsync(ct);
            await LoadMembersAsync(ct);

            Dispatcher.UIThread.Post(InvalidateAllInfoPanelProperties);

            Debug.WriteLine($"[ChatViewModel] InfoPanel data refreshed for chat {_chatId}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatViewModel] RefreshInfoPanelData error: {ex.Message}");
        }
    }

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