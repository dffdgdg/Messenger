using MessengerDesktop.ViewModels.Chat.Managers;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatInfoPanelHandler(ChatContext context, IChatInfoPanelStateStore stateStore,
    ChatMemberLoader memberLoader) : ChatFeatureHandler(context)
{
    [ObservableProperty] private UserDto? _contactUser;
    [ObservableProperty] private bool _isContactOnline;
    [ObservableProperty] private string? _contactLastSeen;

    public bool IsInfoPanelOpen
    {
        get => stateStore.IsOpen;
        set
        {
            if (stateStore.IsOpen == value) return;
            stateStore.IsOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsContactChat => Ctx.Chat?.Type == ChatType.Contact;
    public bool IsGroupChat
        => Ctx.Chat?.Type is ChatType.Chat or ChatType.Department;

    public string InfoPanelTitle => IsContactChat ? "Информация о пользователе" : "Информация о группе";

    public string InfoPanelSubtitle => GetInfoPanelSubtitle();

    private string GetInfoPanelSubtitle()
    {
        if (!IsContactChat)
            return $"{Ctx.Members.Count} участников";

        if (IsContactOnline)
            return "в сети";

        return ContactLastSeen ?? "не в сети";
    }

    public string? ContactAvatar => ContactUser?.Avatar;
    public string? ContactDisplayName => ContactUser?.DisplayName;
    public string? ContactUsername => ContactUser?.Username;
    public string? ContactDepartment => ContactUser?.Department;

    public void Subscribe()
    {
        Ctx.Hub.UserStatusChanged += OnUserStatusChanged;
        Ctx.Hub.UserProfileUpdated += OnUserProfileUpdated;
        Ctx.Hub.MemberJoined += OnMemberJoined;
        Ctx.Hub.MemberLeft += OnMemberLeft;
        Ctx.Members.CollectionChanged += OnMembersCollectionChanged;
    }

    public async Task LoadContactUserAsync()
    {
        var contact = Ctx.Members.FirstOrDefault(m => m.Id != Ctx.CurrentUserId);
        if (contact == null) return;

        ContactUser = contact;
        IsContactOnline = contact.IsOnline;
        ContactLastSeen = FormatLastSeen(contact);

        if (Ctx.Chat != null)
        {
            Ctx.Chat.Name = contact.DisplayName
                ?? contact.Username ?? Ctx.Chat.Name;
            if (!string.IsNullOrEmpty(contact.Avatar))
                Ctx.Chat.Avatar = contact.Avatar;
        }

        InvalidateAll();


        if (!string.IsNullOrWhiteSpace(contact.Department))
            return;

        try
        {
            var profileResult = await Ctx.Api.GetAsync<UserDto>(ApiEndpoints.Users.ById(contact.Id), Ctx.LifetimeToken);
            if (profileResult is not { Success: true, Data: not null })
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (!IsAlive) return;

                ContactUser = profileResult.Data;
                IsContactOnline = profileResult.Data.IsOnline;
                ContactLastSeen = FormatLastSeen(profileResult.Data);

                var memberIndex = Ctx.Members
                    .Select((member, index) => new { member, index })
                    .FirstOrDefault(x => x.member.Id == profileResult.Data.Id)?.index;

                if (memberIndex.HasValue)
                    Ctx.Members[memberIndex.Value] = profileResult.Data;

                InvalidateAll();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InfoPanel] LoadContactUserAsync profile error: {ex.Message}");
        }

    }

    public async Task ReloadMembersAfterEditAsync()
    {
        try
        {
            var freshMembers = await memberLoader.LoadMembersAsync(Ctx.Chat, Ctx.LifetimeToken);

            Dispatcher.UIThread.Post(() =>
            {
                Ctx.Members = freshMembers;
                if (IsContactChat)
                    _ = LoadContactUserAsync();
                InvalidateAll();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InfoPanel] ReloadMembers error: {ex.Message}");
        }
    }

    private void OnUserStatusChanged(int userId, bool isOnline)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;

            if (IsContactChat && ContactUser?.Id == userId)
            {
                IsContactOnline = isOnline;
                ContactUser.IsOnline = isOnline;
                ContactUser.LastOnline = DateTime.UtcNow;
                ContactLastSeen = isOnline ? null : FormatLastSeen(ContactUser);
                OnPropertyChanged(nameof(InfoPanelSubtitle));
            }

            var member = Ctx.Members.FirstOrDefault(m => m.Id == userId);
            if (member != null)
            {
                member.IsOnline = isOnline;
                if (!isOnline) member.LastOnline = DateTime.UtcNow;
                var idx = Ctx.Members.IndexOf(member);
                if (idx >= 0) Ctx.Members[idx] = member;
            }
        });
    }

    private void OnUserProfileUpdated(UserDto updated)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;

            if (IsContactChat && ContactUser?.Id == updated.Id)
            {
                ContactUser = updated;
                IsContactOnline = updated.IsOnline;
                ContactLastSeen = FormatLastSeen(updated);

                if (Ctx.Chat != null)
                {
                    Ctx.Chat.Name = updated.DisplayName
                        ?? updated.Username ?? Ctx.Chat.Name;
                    if (!string.IsNullOrEmpty(updated.Avatar))
                        Ctx.Chat.Avatar = updated.Avatar;
                }

                InvalidateAll();
            }

            for (int i = 0; i < Ctx.Members.Count; i++)
            {
                if (Ctx.Members[i].Id == updated.Id)
                {
                    Ctx.Members[i] = updated;
                    break;
                }
            }
        });
    }

    private void OnMemberJoined(int chatId, UserDto user)
    {
        if (chatId != Ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;
            if (Ctx.Members.All(m => m.Id != user.Id))
                Ctx.Members.Add(user);
        });
    }

    private void OnMemberLeft(int chatId, int userId)
    {
        if (chatId != Ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var member = Ctx.Members.FirstOrDefault(m => m.Id == userId);
            if (member != null) Ctx.Members.Remove(member);
        });
    }

    private void OnMembersCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(InfoPanelSubtitle));

    internal static string? FormatLastSeen(UserDto contact)
    {
        if (contact.IsOnline || !contact.LastOnline.HasValue)
            return null;

        var elapsed = DateTimeOffset.UtcNow - contact.LastOnline.Value;

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

    private void InvalidateAll()
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

    [RelayCommand]
    public void Toggle() => IsInfoPanelOpen = !IsInfoPanelOpen;

    public override void Dispose()
    {
        Ctx.Hub.UserStatusChanged -= OnUserStatusChanged;
        Ctx.Hub.UserProfileUpdated -= OnUserProfileUpdated;
        Ctx.Hub.MemberJoined -= OnMemberJoined;
        Ctx.Hub.MemberLeft -= OnMemberLeft;
        Ctx.Members.CollectionChanged -= OnMembersCollectionChanged;
        base.Dispose();
    }
}