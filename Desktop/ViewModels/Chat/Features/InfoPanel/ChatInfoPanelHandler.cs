using Desktop.Infrastructure.Helpers;
using Desktop.Infrastructure.Media;
using Desktop.ViewModels.Chat.Context;
using Desktop.ViewModels.Chat.Managers;
using Desktop.ViewModels.Chat.Shared;
using Microsoft.Extensions.DependencyInjection;
using Shared.Dto.Online;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Desktop.ViewModels.Chat;

public sealed partial class ChatInfoPanelHandler(ChatContext context, IChatInfoPanelStateStore stateStore, ChatMemberLoader memberLoader, IPlatformService platformService)
    : ChatFeatureHandler(context)
{
    [ObservableProperty] public partial UserDto? ContactUser { get; set; }
    [ObservableProperty] public partial bool IsContactOnline { get; set; }
    [ObservableProperty] public partial string? ContactLastSeen { get; set; }

    [ObservableProperty]
    public partial string MemberSearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PollSearchQuery { get; set; } = string.Empty;

    public ObservableCollection<UserDto> FilteredMembers { get; } = [];
    public ObservableCollection<MessageViewModel> FilteredPolls { get; } = [];

    private IReadOnlyList<MessageViewModel> _allPolls = [];

    #region Photos Pagination

    private const int PhotosPageSize = 30;
    private List<ChatInfoPanelMediaItem> _allPhotos = [];
    private int _photosLoaded = 0;

    public ObservableCollection<ChatInfoPanelMediaItem> PhotosItems { get; } = [];

    public bool HasMorePhotos => _photosLoaded < _allPhotos.Count;

    public string RemainingPhotosText
    {
        get
        {
            if (!HasMorePhotos) return string.Empty;
            var remaining = Math.Min(PhotosPageSize, _allPhotos.Count - _photosLoaded);
            return remaining == 1 ? "Показать ещё 1 фото" : $"Показать ещё {remaining} фото";
        }
    }

    public void SetPhotos(IEnumerable<ChatInfoPanelMediaItem> photos)
    {
        _allPhotos = [.. photos];
        _photosLoaded = 0;
        PhotosItems.Clear();
        LoadMorePhotos();
    }

    [RelayCommand]
    public void LoadMorePhotos()
    {
        if (!HasMorePhotos) return;

        var batch = _allPhotos
            .Skip(_photosLoaded)
            .Take(PhotosPageSize);

        foreach (var item in batch)
            PhotosItems.Add(item);

        _photosLoaded = PhotosItems.Count;

        OnPropertyChanged(nameof(HasMorePhotos));
        OnPropertyChanged(nameof(RemainingPhotosText));
    }

    #endregion

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
    public bool IsGroupChat => Ctx.Chat?.Type != ChatType.Contact;

    public string InfoPanelTitle => IsContactChat ? "Информация о пользователе" : "Информация о группе";

    public string InfoPanelSubtitle => GetInfoPanelSubtitle();

    private string GetInfoPanelSubtitle()
    {
        if (!IsContactChat)
        {
            var count = Ctx.Members.Count;
            return count switch
            {
                0 => "нет участников",
                1 => "1 участник",
                >= 2 and <= 4 => $"{count} участника",
                _ => $"{count} участников"
            };
        }

        if (IsContactOnline) return "в сети";
        return ContactLastSeen ?? "не в сети";
    }

    public string? ContactAvatar => ContactUser?.Avatar;
    public string? ContactDisplayName => ContactUser?.DisplayName;
    public string? ContactUsername => ContactUser?.Username;
    public string? ContactDepartment => ContactUser?.Department;

    #region Property change handlers

    partial void OnMemberSearchQueryChanged(string value) => UpdateFilteredMembers();
    partial void OnPollSearchQueryChanged(string value) => UpdateFilteredPolls();

    #endregion

    #region Members

    public void OnMembersSectionOpened()
    {
        MemberSearchQuery = string.Empty;
        UpdateFilteredMembers();
    }

    private void UpdateFilteredMembers()
    {
        FilteredMembers.Clear();

        var query = MemberSearchQuery?.Trim() ?? string.Empty;
        var source = Ctx.Members;

        if (string.IsNullOrEmpty(query))
        {
            foreach (var m in source)
                FilteredMembers.Add(m);
            return;
        }

        var lower = query.ToLowerInvariant();
        foreach (var m in source)
        {
            var matchName = m.DisplayName?.Contains(lower, StringComparison.OrdinalIgnoreCase) == true;
            var matchUsername = m.Username?.Contains(lower, StringComparison.OrdinalIgnoreCase) == true;
            var matchDept = m.Department?.Contains(lower, StringComparison.OrdinalIgnoreCase) == true;

            if (matchName || matchUsername || matchDept)
                FilteredMembers.Add(m);
        }
    }

    #endregion

    #region Polls

    public void OnPollsSectionOpened(IEnumerable<MessageViewModel> polls)
    {
        _allPolls = [.. polls];
        PollSearchQuery = string.Empty;
        UpdateFilteredPolls();
    }

    public void SetPolls(IReadOnlyList<MessageViewModel> polls)
    {
        _allPolls = polls;
        UpdateFilteredPolls();
    }

    private void UpdateFilteredPolls()
    {
        FilteredPolls.Clear();

        var query = PollSearchQuery?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(query))
        {
            foreach (var m in _allPolls)
                FilteredPolls.Add(m);
            return;
        }

        var lower = query.ToLowerInvariant();
        foreach (var m in _allPolls)
        {
            if (m.Content?.Contains(lower, StringComparison.OrdinalIgnoreCase) == true ||
                m.Poll?.Options?.Any(o => o.OptionText?.Contains(lower, StringComparison.OrdinalIgnoreCase) == true) == true)
            {
                FilteredPolls.Add(m);
            }
        }
    }

    #endregion

    #region Hub subscription
    public event Action<ObservableCollection<UserDto>, ObservableCollection<UserDto>>? MembersReplaced;

    private ObservableCollection<UserDto> _members = [];
    public ObservableCollection<UserDto> Members
    {
        get => _members;
        set
        {
            var old = _members;
            _members = value;
            MembersReplaced?.Invoke(old, value);
            OnPropertyChanged();
        }
    }
    private ObservableCollection<UserDto>? _subscribedMembersCollection;
    public void Subscribe()
    {
        Ctx.Hub.UserStatusChanged -= OnUserStatusChanged;
        Ctx.Hub.UserProfileUpdated -= OnUserProfileUpdated;
        Ctx.Hub.MemberJoined -= OnMemberJoined;
        Ctx.Hub.MemberLeft -= OnMemberLeft;

        _subscribedMembersCollection?.CollectionChanged -= OnMembersCollectionChanged;

        _subscribedMembersCollection = Ctx.Members;
        _subscribedMembersCollection.CollectionChanged += OnMembersCollectionChanged;

        Ctx.Hub.UserStatusChanged += OnUserStatusChanged;
        Ctx.Hub.UserProfileUpdated += OnUserProfileUpdated;
        Ctx.Hub.MemberJoined += OnMemberJoined;
        Ctx.Hub.MemberLeft += OnMemberLeft;

        Ctx.MembersReplaced += OnMembersReplaced;

        UpdateFilteredMembers();
    }

    private void OnMembersReplaced(ObservableCollection<UserDto> old, ObservableCollection<UserDto> next)
    {
        _subscribedMembersCollection?.CollectionChanged -= OnMembersCollectionChanged;

        _subscribedMembersCollection = next;
        _subscribedMembersCollection.CollectionChanged += OnMembersCollectionChanged;

        UpdateFilteredMembers();
    }

    protected override void DisposeManaged()
    {
        Ctx.Hub.UserStatusChanged -= OnUserStatusChanged;
        Ctx.Hub.UserProfileUpdated -= OnUserProfileUpdated;
        Ctx.Hub.MemberJoined -= OnMemberJoined;
        Ctx.Hub.MemberLeft -= OnMemberLeft;
        Ctx.MembersReplaced -= OnMembersReplaced;
        _subscribedMembersCollection?.CollectionChanged -= OnMembersCollectionChanged;
        _subscribedMembersCollection = null;

        FilteredPolls.Clear();
        FilteredMembers.Clear();
        PhotosItems.Clear();
        _allPolls = [];
        _allPhotos = [];
    }
    #endregion

    #region Contact

    public async Task LoadContactUserAsync()
    {
        var contact = Ctx.Members.FirstOrDefault(m => m.Id != Ctx.CurrentUserId);
        if (contact == null) return;

        ContactUser = contact;
        IsContactOnline = contact.IsOnline;
        ContactLastSeen = FormatLastSeen(contact);

        if (Ctx.Chat != null)
        {
            Ctx.Chat.Name = contact.DisplayName ?? contact.Username ?? Ctx.Chat.Name;
            if (!string.IsNullOrEmpty(contact.Avatar))
                Ctx.Chat.Avatar = contact.Avatar;
        }

        InvalidateAll();

        if (!string.IsNullOrWhiteSpace(contact.Department)) return;

        try
        {
            var profileResult = await Ctx.Api.GetAsync<UserDto>(ApiEndpoints.Users.ById(contact.Id), Ctx.LifetimeToken);

            if (profileResult is not { Success: true, Data: not null }) return;

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
        catch (OperationCanceledException) { /* Отменено */ }
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
                UpdateFilteredMembers();
                if (IsContactChat) _ = LoadContactUserAsync();
                InvalidateAll();
            });
        }
        catch (OperationCanceledException) { /* Отменено */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InfoPanel] ReloadMembers error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CopyUsername()
    {
        if (string.IsNullOrEmpty(ContactUsername)) return;
        await platformService.CopyToClipboardAsync(ContactUsername);
    }

    #endregion

    #region Hub event handlers

    private void OnUserStatusChanged(UserStatusDto status) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsAlive) return;
        UpdateContactStatus(status.UserId, status.IsOnline);
        UpdateMemberStatus(status.UserId, status.IsOnline);
        UpdateMemberStatusType(status);
    });

    private void UpdateMemberStatusType(UserStatusDto status)
    {
        var member = Ctx.Members.FirstOrDefault(m => m.Id == status.UserId);
        if (member == null) return;

        member.StatusType = status.StatusType;
        member.StatusExpiresAt = status.StatusExpiresAt;

        var idx = Ctx.Members.IndexOf(member);
        if (idx >= 0) Ctx.Members[idx] = member;
    }

    private void UpdateContactStatus(int userId, bool isOnline)
    {
        if (!IsContactChat || ContactUser?.Id != userId) return;

        IsContactOnline = isOnline;
        ContactUser.IsOnline = isOnline;
        ContactUser.LastOnline = DateTime.UtcNow;
        ContactLastSeen = isOnline ? null : FormatLastSeen(ContactUser);
        OnPropertyChanged(nameof(InfoPanelSubtitle));
    }

    private void UpdateMemberStatus(int userId, bool isOnline)
    {
        var member = Ctx.Members.FirstOrDefault(m => m.Id == userId);
        if (member == null) return;

        member.IsOnline = isOnline;
        if (!isOnline) member.LastOnline = DateTime.UtcNow;

        var idx = Ctx.Members.IndexOf(member);
        if (idx >= 0) Ctx.Members[idx] = member;
    }

    private void OnUserProfileUpdated(UserDto updated) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsAlive) return;
        UpdateContactProfile(updated);
        ReplaceMemberInList(updated);
        UpdateFilteredMembers();
    });

    private void UpdateContactProfile(UserDto updated)
    {
        if (!IsContactChat || ContactUser?.Id != updated.Id) return;

        var oldAvatar = ContactUser?.Avatar;
        var avatarChanged = oldAvatar != updated.Avatar;

        if (avatarChanged)
        {
            if (!string.IsNullOrEmpty(oldAvatar))
                InvalidateAvatarCache(oldAvatar);
            if (!string.IsNullOrEmpty(updated.Avatar))
                InvalidateAvatarCache(updated.Avatar);

            ContactUser = null;
            InvalidateAll();
        }

        if (avatarChanged && !string.IsNullOrEmpty(updated.Avatar))
            updated.Avatar = AvatarHelper.WithFreshCacheBuster(updated.Avatar);

        ContactUser = updated;
        IsContactOnline = updated.IsOnline;
        ContactLastSeen = FormatLastSeen(updated);
        UpdateChatHeaderFromContact(updated);
        InvalidateAll();
    }

    private void UpdateChatHeaderFromContact(UserDto contact)
    {
        if (Ctx.Chat == null) return;

        var oldChatAvatar = Ctx.Chat.Avatar;
        Ctx.Chat.Name = contact.DisplayName ?? contact.Username ?? Ctx.Chat.Name;

        if (!string.IsNullOrEmpty(contact.Avatar))
            Ctx.Chat.Avatar = contact.Avatar;

        if (oldChatAvatar != Ctx.Chat.Avatar)
            InvalidateAvatarCache(oldChatAvatar);
    }

    private static void InvalidateAvatarCache(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        try
        {
            App.Current.Services.GetRequiredService<AuthenticatedImageLoader>().InvalidateByRelativePath(relativePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InfoPanel] InvalidateAvatarCache error: {ex.Message}");
        }
    }

    private void ReplaceMemberInList(UserDto updated)
    {
        for (var i = 0; i < Ctx.Members.Count; i++)
        {
            if (Ctx.Members[i].Id != updated.Id) continue;
            Ctx.Members[i] = updated;
            return;
        }
    }

    private void OnMemberJoined(int chatId, UserDto user)
    {
        if (chatId != Ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;
            if (Ctx.Members.All(m => m.Id != user.Id))
            {
                Ctx.Members.Add(user);
                UpdateFilteredMembers();
            }
        });
    }

    private void OnMemberLeft(int chatId, int userId)
    {
        if (chatId != Ctx.ChatId) return;

        Dispatcher.UIThread.Post(() =>
        {
            var member = Ctx.Members.FirstOrDefault(m => m.Id == userId);
            if (member == null) return;
            Ctx.Members.Remove(member);
            UpdateFilteredMembers();
        });
    }

    private void OnMembersCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InfoPanelSubtitle));
        UpdateFilteredMembers();
    }

    #endregion

    #region Helpers

    internal static string? FormatLastSeen(UserDto contact)
    {
        if (contact.IsOnline || !contact.LastOnline.HasValue) return null;

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

    #endregion

    [RelayCommand]
    public void Toggle() => IsInfoPanelOpen = !IsInfoPanelOpen;
}