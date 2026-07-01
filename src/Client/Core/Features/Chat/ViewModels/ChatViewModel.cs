using Avalonia.Input;
using Core.Dialog.Confirm;
using Core.Dialog.UserProfile;
using Core.Features.Chat.ViewModels.Commands;
using Core.Features.Chat.ViewModels.Composer;
using Core.Features.Chat.ViewModels.Context;
using Core.Features.Chat.ViewModels.Handlers.Call;
using Core.Features.Chat.ViewModels.Handlers.Collaboration;
using Core.Features.Chat.ViewModels.Handlers.Editing;
using Core.Features.Chat.ViewModels.Handlers.InfoPanel;
using Core.Features.Chat.ViewModels.Handlers.Media;
using Core.Features.Chat.ViewModels.Handlers.Notifications;
using Core.Features.Chat.ViewModels.Handlers.Pinned;
using Core.Features.Chat.ViewModels.Handlers.Search;
using Core.Features.Chat.ViewModels.Managers;
using Core.Features.Chat.ViewModels.Messages;
using Core.Features.Chat.ViewModels.Navigation;
using Core.Features.Chat.ViewModels.Permissions;
using Core.Features.Chat.ViewModels.Polls;
using Core.Features.ChatList.ViewModels;
using Core.Features.ChatList.ViewModels.Factories;
using Core.Features.MessageList.ViewModels.Managers;
using Core.Features.MessageList.ViewModels.Scroll;
using Core.Infrastructure.Media;
using Core.Services.Media.Files;
using System.Diagnostics;

namespace Core.Features.Chat.ViewModels;

public sealed partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    public enum InfoSectionType { None, Photos, Files, Polls, Members, Pinned }

    #region Дочерние компоненты — публичные, View биндится напрямую

    public ChatContext Context { get; }
    public ChatListViewModel Parent { get; }
    public ChatMessageManager MessageManager { get; }
    public ChatAttachmentManager Attachments { get; }
    public ChatMemberLoader MemberLoader { get; }
    public ChatComposer Composer { get; }
    public ChatScrollCoordinator Scroll { get; }
    public ChatPermissionsManager Permissions { get; }
    public ChatSectionManager Sections { get; }
    public ChatEditDeleteHandler EditDelete { get; }
    public ChatReplyHandler Reply { get; }
    public ChatForwardHandler Forward { get; }
    public ChatTypingHandler Typing { get; }
    public ChatVoiceHandler Voice { get; }
    public ChatInfoPanelHandler InfoPanel { get; }
    public ChatSearchHandler Search { get; }
    public ChatNotificationHandler Notification { get; }
    public ChatPinnedHandler Pinned { get; }
    public ChatCallHandler Call { get; }

    #endregion

    #region Приватные поля

    private readonly IChatNavigator _navigator;
    private readonly ChatHubSubscriber _hubSubscriber;
    private readonly TaskCompletionSource _initTcs = new();
    private readonly int? _targetMessageId;
    private readonly IFileDownloadService _fileDownloadService;
    private readonly IFileDownloadStateService? _fileDownloadStateService;
    private readonly INotificationService _notificationService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IAudioRecorderService _audioRecorderService;
    private readonly AuthenticatedImageLoader _imageLoader;
    private readonly IDownloadManager _downloadManager;

    #endregion

    #region Observable свойства — только то, что принадлежит VM

    [ObservableProperty] public partial bool IsInitialLoading { get; set; } = true;
    [ObservableProperty] public partial bool IsLoadingOlderMessages { get; set; }
    [ObservableProperty] public partial bool IsLoadingNewerMessages { get; set; }
    [ObservableProperty] public partial bool HasNewMessages { get; set; }
    [ObservableProperty] public partial bool IsScrolledToBottom { get; set; } = true;
    [ObservableProperty] public partial int UnreadCount { get; set; }
    [ObservableProperty] public partial bool IsInfoSectionOpen { get; set; }
    [ObservableProperty] public partial string InfoSectionTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoSectionType CurrentInfoSection { get; set; }
    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial UserProfileDialogViewModel? UserProfileDialog { get; set; }

    #endregion

    #region Вычисляемые свойства VM

    public bool ShowScrollToBottom => !IsScrolledToBottom;

    public bool HasContactInfoMediaSection =>
        Sections.HasPhotos || Sections.HasFiles || Pinned.HasPinnedMessages;

    public bool HasGroupInfoMediaSection =>
        Sections.HasPhotos || Sections.HasFiles || Pinned.HasPinnedMessages || Sections.HasPolls;

    public bool ShowPinnedSection => CurrentInfoSection == InfoSectionType.Pinned;
    public bool ShowPhotosSection => CurrentInfoSection == InfoSectionType.Photos;
    public bool ShowFilesSection => CurrentInfoSection == InfoSectionType.Files;
    public bool ShowPollsSection => CurrentInfoSection == InfoSectionType.Polls;
    public bool ShowMembersSection => CurrentInfoSection == InfoSectionType.Members;

    public bool IsDepartmentChat => Chat?.Type == ChatType.Department;
    public bool IsDepartmentHeadsChat => Chat?.Type == ChatType.DepartmentHeads;
    public bool IsDepartmentScopedChat => IsDepartmentChat || IsDepartmentHeadsChat;

    public string MemberCountText
    {
        get
        {
            if (Chat?.Type == ChatType.Contact)
            {
                if (InfoPanel.IsContactOnline) return "в сети";
                return InfoPanel.ContactLastSeen ?? string.Empty;
            }

            var count = Context.Members?.Count ?? 0;
            return count switch
            {
                0 => string.Empty,
                1 => "1 участник",
                var n when n % 100 is >= 11 and <= 14 => $"{n} участников",
                var n when n % 10 == 1 => $"{n} участник",
                var n when n % 10 is 2 or 3 or 4 => $"{n} участника",
                var n => $"{n} участников"
            };
        }
    }

    public ChatDto? Chat
    {
        get => Context.Chat;
        set => Context.Chat = value;
    }

    public ObservableCollection<UserDto> MembersPreView { get; } = [];

    public bool HasInitialMessageTarget => _targetMessageId.HasValue;
    public Action? RequestGoBackToList { get; set; }

    #endregion

    #region Events

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    #endregion

    #region Constructor

    public ChatViewModel(
    ChatDto initialChat,
    ChatListViewModel parent,
    IChatNavigator navigator,
    ChatCoreServices core,
    MediaServices media,
    CallServices calls,
    CacheServices cache,
    int? targetMessageId = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(initialChat);

        _fileDownloadService = media.FileDownloadService;
        _fileDownloadStateService = media.FileDownloadStateService;
        _notificationService = core.NotificationService;
        _audioPlayerService = media.AudioPlayer;
        _audioRecorderService = media.AudioRecorder;
        _imageLoader = core.ImageLoader;
        _downloadManager = media.DownloadManager;

        var currentUserId = core.AuthManager.Session.UserId
            ?? throw new InvalidOperationException("Пользователь не авторизован");

        UserId = currentUserId;
        _targetMessageId = targetMessageId;

        Context = new ChatContext(initialChat.Id, currentUserId, core, media, cache)
        {
            Chat = initialChat,
            Navigator = navigator,
            IsSystemAdmin = core.AuthManager.Session.IsAdmin
        };

        Context.ScrollToMessageRequested += (msg, hl) => ScrollToMessageRequested?.Invoke(msg, hl);
        Context.ScrollToIndexRequested += (idx, hl) => ScrollToIndexRequested?.Invoke(idx, hl);
        Context.ScrollToBottomRequested += () => ScrollToBottomRequested?.Invoke();
        Context.RequestRefreshCounters = () => _ = Sections.RefreshCountsAsync();

        core.GlobalHub.SetCurrentChat(initialChat.Id);

        var chatCommands = new ChatCommands
        {
            OpenProfile = OpenProfileCommand,
            ShowPollResults = new AsyncRelayCommand<PollViewModel>(async vm =>
            {
                if (vm?.CurrentPollDto == null) return;
                var dialog = new PollResultsDialogViewModel(
                    vm.CurrentPollDto, Context.Members, Context.Api);
                await Context.Dialogs.ShowAsync(dialog);
                await dialog.TriggerInitializeAsync();
            })
        };

        MessageManager = new ChatMessageManager(Context, media, chatCommands, OpenMentionProfileCommand);
        Attachments = new ChatAttachmentManager(initialChat.Id, core.ApiClient, media.PlatformService);
        MemberLoader = new ChatMemberLoader(initialChat.Id, currentUserId, core.ApiClient);

        EditDelete = new ChatEditDeleteHandler(Context);
        Reply = new ChatReplyHandler(Context, MessageManager);
        Forward = new ChatForwardHandler(Context);
        Typing = new ChatTypingHandler(Context);
        Voice = new ChatVoiceHandler(Context, () => Reply.CancelReply());
        InfoPanel = new ChatInfoPanelHandler(Context, core.ChatInfoPanelStateStore, MemberLoader, media.PlatformService);
        Search = new ChatSearchHandler(Context, MessageManager);
        Notification = new ChatNotificationHandler(Context);

        Pinned = new ChatPinnedHandler(Context, media.FileDownloadService, media.FileDownloadStateService,
            media.AudioPlayer, core.NotificationService);

        Call = new ChatCallHandler(Context, calls.CallService, calls.CallHub,
            openCallUi: () => { Parent.OpenCallUi(); return Task.CompletedTask; },
            showCallView: (state, name, isGroup) =>
            {
                Parent.ShowCallView(state, name, isGroup);
                return Task.CompletedTask;
            });

        Composer = new ChatComposer(Context, Attachments, Reply, Forward, EditDelete, MessageManager);

        Scroll = new ChatScrollCoordinator(Context, MessageManager);
        Permissions = new ChatPermissionsManager(Context, core.DialogService);
        Sections = new ChatSectionManager(Context, InfoPanel, MessageManager, media.FileDownloadService,
            media.FileDownloadStateService, core.NotificationService, currentUserId);

        chatCommands.Edit = EditDelete.StartEditCommand;
        chatCommands.Copy = EditDelete.CopyMessageTextCommand;
        chatCommands.Delete = EditDelete.DeleteMessageCommand;
        chatCommands.TogglePin = EditDelete.TogglePinCommand;
        chatCommands.Reply = Reply.StartReplyCommand;
        chatCommands.ScrollToReply = Reply.ScrollToReplyOriginalCommand;
        chatCommands.Forward = Forward.StartForwardCommand;

        InfoPanel.OpenStateChanged += isOpen =>
        {
            if (isOpen) RefreshInfoPanelLists();
            else OpenInfoSection(InfoSectionType.None);
        };

        SubscribeToChildPropertyChanges();

        _hubSubscriber = new ChatHubSubscriber(Context, MessageManager,
            count => UnreadCount = count, OnHubReconnectedAsync);
        _hubSubscriber.Subscribe();

        Context.Hub.ChatUpdated += OnChatUpdated;

        _ = InitializeAsync();
    }

    private void SubscribeToChildPropertyChanges()
    {
        // MemberCountText зависит от InfoPanel.IsContactOnline и ContactLastSeen
        InfoPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ChatInfoPanelHandler.IsContactOnline)
                               or nameof(ChatInfoPanelHandler.ContactLastSeen))
                OnPropertyChanged(nameof(MemberCountText));
        };

        // HasGroupInfoMediaSection / HasContactInfoMediaSection зависят от Sections и Pinned
        Sections.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ChatSectionManager.HasPhotos)
                               or nameof(ChatSectionManager.HasFiles)
                               or nameof(ChatSectionManager.HasPolls))
            {
                OnPropertyChanged(nameof(HasGroupInfoMediaSection));
                OnPropertyChanged(nameof(HasContactInfoMediaSection));
            }
        };

        Pinned.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatPinnedHandler.HasPinnedMessages))
            {
                OnPropertyChanged(nameof(HasGroupInfoMediaSection));
                OnPropertyChanged(nameof(HasContactInfoMediaSection));
            }
        };

        // MemberCountText зависит от количества участников
        Context.Members.CollectionChanged += (_, _) =>
        {
            ScheduleRefreshInfoPanelLists();
            OnPropertyChanged(nameof(MemberCountText));
        };

        Context.MembersReplaced += (_, _) =>
        {
            ScheduleRefreshInfoPanelLists();
            OnPropertyChanged(nameof(MemberCountText));
        };

        MessageManager.Messages.CollectionChanged += (_, _) =>
            ScheduleRefreshInfoPanelLists();
    }

    #endregion

    #region Initialization

    private async Task InitializeAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            IsInitialLoading = true;
            Debug.WriteLine($"[ChatVM] Init start chat={Context.ChatId}");

            var chatTask = Context.Api.GetAsync<ChatDto>(
                ApiEndpoints.Chats.ById(Context.ChatId), Context.LifetimeToken);
            var readInfoTask = Context.Hub.GetReadInfoAsync(Context.ChatId);
            var notificationTask = Notification.LoadSettingsAsync(Context.LifetimeToken);

            await Task.WhenAll(chatTask, readInfoTask, notificationTask);

            var chatResult = chatTask.Result;
            if (chatResult is { Success: true, Data: not null })
            {
                Context.Chat = chatResult.Data;
                Context.CurrentUserRole = chatResult.Data.CurrentUserRole;
            }
            else
            {
                throw new HttpRequestException($"Не удалось загрузить чат: {chatResult.Error}");
            }

            await Call.InitAsync(Context.LifetimeToken);
            MessageManager.SetReadInfo(readInfoTask.Result);

            var membersTask = MemberLoader.LoadMembersAsync(Context.Chat, Context.LifetimeToken);
            var pinnedTask = Pinned.LoadInitialAsync(Context.LifetimeToken);
            var countsTask = Context.Api.GetAsync<ChatCountsDto>(
                ApiEndpoints.Messages.Counts(Context.ChatId), Context.LifetimeToken);

            await Task.WhenAll(membersTask, pinnedTask, countsTask);

            var countsResult = countsTask.Result;
            if (countsResult is { Success: true, Data: not null })
                Sections.ApplyCounts(countsResult.Data);
            else
                Debug.WriteLine($"[ChatVM] Не удалось загрузить счётчики: {countsResult?.Error}");

            Context.Members = membersTask.Result;
            Permissions.Refresh();

            if (InfoPanel.IsContactChat)
                await InfoPanel.LoadContactUserAsync();

            var scrollToIndex = await MessageManager.LoadInitialMessagesAsync(
                _targetMessageId, Context.LifetimeToken);

            if (_targetMessageId.HasValue && scrollToIndex.HasValue)
                Context.RequestScrollToIndex(scrollToIndex.Value, highlight: true);
            else if (scrollToIndex.HasValue && scrollToIndex < MessageManager.Messages.Count - 1)
                Context.RequestScrollToIndex(scrollToIndex.Value, highlight: true);
            else
                Context.RequestScrollToBottom();

            RefreshInfoPanelLists();
            Voice.Initialize(_audioRecorderService);
            InfoPanel.Subscribe();

            Debug.WriteLine($"[ChatVM] Init done chat={Context.ChatId} за {sw.ElapsedMilliseconds}ms");
            _initTcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _initTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Ошибка инициализации: {ex.GetType().Name}: {ex.Message}");
            ErrorMessage = $"Не удалось открыть чат: {ex.Message}";
            DisposeCore();
            _initTcs.TrySetException(ex);
        }
        finally
        {
            IsInitialLoading = false;
        }
    }

    #endregion

    #region Partial hooks

    partial void OnIsScrolledToBottomChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowScrollToBottom));
        if (!value) return;

        HasNewMessages = false;
        UnreadCount = 0;
        _ = Scroll.MarkMessagesAsReadAsync();
    }

    partial void OnCurrentInfoSectionChanged(InfoSectionType value)
    {
        OnPropertyChanged(nameof(ShowPhotosSection));
        OnPropertyChanged(nameof(ShowFilesSection));
        OnPropertyChanged(nameof(ShowPollsSection));
        OnPropertyChanged(nameof(ShowMembersSection));
        OnPropertyChanged(nameof(ShowPinnedSection));
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task SendMessage()
    {
        if (Context.IsDisposed) return;

        if (EditDelete.IsEditMode)
        {
            await EditDelete.SaveEditCommand.ExecuteAsync(null);
            return;
        }

        await Composer.SendMessageAsync();
    }

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading) return;
        IsLoadingOlderMessages = true;
        try { await MessageManager.LoadOlderMessagesAsync(Context.LifetimeToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[ChatVM] LoadOlderMessages error: {ex.Message}"); }
        finally { IsLoadingOlderMessages = false; }
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading || !MessageManager.HasMoreNewer) return;
        IsLoadingNewerMessages = true;
        try { await MessageManager.LoadNewerMessagesAsync(Context.LifetimeToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[ChatVM] LoadNewerMessages error: {ex.Message}"); }
        finally { IsLoadingNewerMessages = false; }
    }

    [RelayCommand]
    private void ToggleInfoPanel() => InfoPanel.IsInfoPanelOpen = !InfoPanel.IsInfoPanelOpen;

    [RelayCommand]
    private void OpenPhotosSection() => OpenInfoSection(InfoSectionType.Photos);

    [RelayCommand]
    private void OpenFilesSection() => OpenInfoSection(InfoSectionType.Files);

    [RelayCommand]
    private void OpenPollsSection() => OpenInfoSection(InfoSectionType.Polls);

    [RelayCommand]
    private void OpenMembersSection() => OpenInfoSection(InfoSectionType.Members);

    [RelayCommand]
    private void CloseInfoSection() => OpenInfoSection(InfoSectionType.None);

    [RelayCommand]
    private async Task OpenPinnedSection()
    {
        if (!InfoPanel.IsInfoPanelOpen) InfoPanel.IsInfoPanelOpen = true;
        OpenInfoSection(InfoSectionType.Pinned);
        await Pinned.ReloadAsync(Context.LifetimeToken);
    }

    [RelayCommand]
    private async Task ScrollToPinnedMessage()
    {
        if (Pinned.PinnedBannerMessage == null) return;
        await Search.ScrollToMessageAsync(Pinned.PinnedBannerMessage.Id);
    }

    [RelayCommand]
    private async Task OpenInfoSectionMessageAsync(MessageViewModel? message)
    {
        if (message == null) return;
        IsInfoSectionOpen = false;
        await Search.ScrollToMessageAsync(message.Id);
    }

    [RelayCommand]
    private void ScrollToBottom()
    {
        Context.RequestScrollToBottom();
        HasNewMessages = false;
        UnreadCount = 0;
    }

    [RelayCommand]
    private async Task ScrollToLatest()
    {
        HasNewMessages = false;
        IsScrolledToBottom = true;
        await Scroll.MarkMessagesAsReadAsync();
    }

    [RelayCommand]
    private void RemoveAttachment(LocalFileAttachment attachment) => Attachments.Remove(attachment);

    [RelayCommand]
    private void InsertEmoji(string emoji) => Composer.InsertEmoji(emoji);

    [RelayCommand]
    private void SelectMention(UserDto? user) => Composer.SelectMention(user);

    [RelayCommand]
    private async Task AttachFile()
    {
        if (!await Attachments.PickAndAddFilesAsync())
            ErrorMessage = "Не удалось выбрать файлы";
    }

    [RelayCommand]
    private void GoBackToList() => RequestGoBackToList?.Invoke();

    [RelayCommand]
    private void LoadMorePhotos() => InfoPanel.LoadMorePhotos();

    [RelayCommand]
    private Task StartOrJoinCallAsync() => Call.StartOrJoinCallCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task OpenCreatePoll()
        => await _navigator.ShowPollDialogAsync(Context.ChatId,
            () => MessageManager.LoadInitialMessagesAsync(null));

    [RelayCommand]
    private async Task OpenEditChat()
    {
        if (!InfoPanel.IsGroupChat || Context.Chat == null || !Permissions.CanEditGroupChat) return;

        await _navigator.ShowEditGroupDialogAsync(Context.Chat, updatedChat =>
        {
            Context.Chat = updatedChat;
            Permissions.Refresh();
            Parent.UpdateChatInList(updatedChat);
            _ = InfoPanel.ReloadMembersAfterEditAsync();
        });
    }

    [RelayCommand]
    public async Task OpenProfile(int userId)
        => await _navigator.ShowUserProfileAsync(userId);

    [RelayCommand]
    private async Task OpenMentionProfile(string? mention)
    {
        if (string.IsNullOrWhiteSpace(mention)) return;
        var normalizedUsername = mention.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(normalizedUsername)) return;

        var user = Context.Members.FirstOrDefault(m =>
            string.Equals(m.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

        if (user?.Id > 0)
            await _navigator.ShowUserProfileAsync(user.Id);
    }

    [RelayCommand]
    private async Task LeaveChat()
    {
        if (!Permissions.CanLeaveChat)
        {
            ErrorMessage = "Владелец чата не может покинуть чат";
            return;
        }

        var confirmDialog = new ConfirmDialogViewModel(
            "Покинуть группу",
            "Вы уверены, что хотите покинуть группу?",
            "Покинуть", "Отмена");

        await Context.Dialogs.ShowAsync(confirmDialog);
        if (!await confirmDialog.Result) return;

        await SafeExecuteAsync(async ct =>
        {
            var result = await Context.Api.DeleteAsync(ApiEndpoints.Chats.Leave(Context.ChatId, UserId), ct);

            if (result.Success)
            {
                SuccessMessage = "Вы покинули чат";
                Parent.SelectedChat = null;
                await Parent.LoadChats();
            }
            else
            {
                ErrorMessage = $"Не удалось выйти из чата: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task CopyUsername()
        => await InfoPanel.CopyUsernameCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task ToggleMute()
        => await Notification.ToggleCommand.ExecuteAsync(null);

    #endregion

    #region UI Helpers

    public bool HandleMentionNavigationKey(Key key) => Composer.HandleNavigationKey(key);
    public void OnComposerSelectionChanged(int caretIndex) => Composer.OnCaretChanged(caretIndex);

    private static readonly Dictionary<InfoSectionType, string> InfoSectionTitles = new()
    {
        [InfoSectionType.Photos] = "Медиа",
        [InfoSectionType.Files] = "Документы",
        [InfoSectionType.Polls] = "Опросы",
        [InfoSectionType.Members] = "Участники",
        [InfoSectionType.Pinned] = "Закреплённые сообщения"
    };

    private void OpenInfoSection(InfoSectionType section)
    {
        if (section == InfoSectionType.None)
        {
            IsInfoSectionOpen = false;
            CurrentInfoSection = InfoSectionType.None;
            InfoSectionTitle = string.Empty;
            Sections.ClearAll();
            RebuildMembersPreView();
            return;
        }

        CurrentInfoSection = section;
        InfoSectionTitle = InfoSectionTitles.GetValueOrDefault(section, string.Empty);
        IsInfoSectionOpen = true;

        switch (section)
        {
            case InfoSectionType.Photos: _ = Sections.LoadPhotosAsync(); break;
            case InfoSectionType.Files: _ = Sections.LoadFilesAsync(); break;
            case InfoSectionType.Polls:
                _ = Sections.LoadPollsAsync();
                InfoPanel.OnPollsSectionOpened(Sections.PollMessages);
                break;
            case InfoSectionType.Members:
                RebuildMembersPreView();
                InfoPanel.OnMembersSectionOpened();
                break;
        }
    }

    private void RebuildMembersPreView()
    {
        MembersPreView.Clear();
        foreach (var member in Context.Members
            .OrderByDescending(m => m.IsOnline)
            .ThenBy(m => m.DisplayName ?? m.Username ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .Take(5))
        {
            MembersPreView.Add(member);
        }
    }

    #endregion

    #region Scroll & Reading

    public void RequestScrollToBottom() => Context.RequestScrollToBottom();
    public Task OnMessageVisibleAsync(MessageViewModel message) => Scroll.OnMessageVisibleAsync(message);
    public Task MarkMessagesAsReadAsync() => Scroll.MarkMessagesAsReadAsync();
    public Task OnMessagesVisibleAsync() => Scroll.MarkMessagesAsReadAsync();
    public Task ScrollToMessageAsync(int messageId) => Search.ScrollToMessageAsync(messageId);
    public void ScrollToMessageFromSearch(MessageViewModel message) => Context.RequestScrollToMessage(message, true);
    public void ScrollToIndexFromSearch(int index) => Context.RequestScrollToIndex(index, true);
    public void ScrollToMessageSilent(MessageViewModel message) => Context.RequestScrollToMessage(message, false);
    public void ScrollToIndexSilent(int index) => Context.RequestScrollToIndex(index, false);

    #endregion

    #region Chat Updates

    private void OnChatUpdated(ChatUpdateEventDto update)
    {
        if (update.Id != Context.ChatId || Context.IsDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (Context.IsDisposed) return;

            var oldAvatar = Context.Chat?.Avatar;

            if (Context.Chat != null)
            {
                Context.Chat.Name = update.Name;
                Context.Chat.ShowHistoryForNewMembers = update.ShowHistoryForNewMembers;
                Context.Chat.Type = update.Type;
            }

            if (update.CurrentUserRole.HasValue)
            {
                Context.CurrentUserRole = update.CurrentUserRole.Value;
                Permissions.Refresh();
            }

            if (oldAvatar != update.Avatar)
            {
                if (!string.IsNullOrEmpty(oldAvatar))
                {
                    try { _imageLoader.InvalidateByRelativePath(oldAvatar); }
                    catch (Exception ex)
                    { Debug.WriteLine($"[ChatVM] Avatar invalidate error: {ex.Message}"); }
                }
                Context.Chat ??= new ChatDto();
                Context.Chat.Avatar = update.Avatar;
            }

            OnPropertyChanged(nameof(Chat));
        });
    }

    public void OnGlobalRoleChanged(UserRole role) => Dispatcher.UIThread.Post(() =>
    {
        if (Context.IsDisposed) return;
        Context.IsSystemAdmin = role == UserRole.Admin;
        Permissions.Refresh();
    });

    public void ApplyChatRoleUpdate(ChatRole newRole) => Dispatcher.UIThread.Post(() =>
    {
        if (Context.IsDisposed) return;
        Context.CurrentUserRole = newRole;
        Permissions.Refresh();
    });

    #endregion

    #region Reconnect

    public Task WaitForInitializationAsync() => _initTcs.Task;

    private async Task OnHubReconnectedAsync()
    {
        try
        {
            var ct = Context.LifetimeToken;
            await Task.WhenAll(
                MessageManager.GapFillAfterReconnectAsync(ct),
                RefreshInfoPanelAsync(ct));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Ошибка при переподключении: {ex.Message}");
        }
    }

    private async Task RefreshInfoPanelAsync(CancellationToken ct)
    {
        try
        {
            var chatResult = await Context.Api.GetAsync<ChatDto>(
                ApiEndpoints.Chats.ById(Context.ChatId), ct);

            if (chatResult is { Success: true, Data: not null })
            {
                Context.Chat = chatResult.Data;
                Permissions.Refresh();
            }

            await InfoPanel.ReloadMembersAfterEditAsync();
            RefreshInfoPanelLists();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Не удалось обновить инфопанель: {ex.Message}");
        }
    }

    #endregion

    #region Refresh helpers

    private CancellationTokenSource? _refreshDebounce;

    private void RefreshInfoPanelLists()
    {
        if (Context.IsDisposed) return;
        RebuildMembersPreView();
    }

    private void ScheduleRefreshInfoPanelLists()
    {
        _refreshDebounce?.Cancel();
        _refreshDebounce?.Dispose();
        _refreshDebounce = new CancellationTokenSource();
        var token = _refreshDebounce.Token;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (!token.IsCancellationRequested)
                    RefreshInfoPanelLists();
            }
            catch (OperationCanceledException) { }
        });
    }

    #endregion

    #region Dispose

    private int _disposeStartedFlag;

    private void DisposeCore()
    {
        if (Interlocked.CompareExchange(ref _disposeStartedFlag, 1, 0) != 0) return;
        if (Context.IsDisposed) return;

        DisposeCommonResources();
        try { _ = MessageManager.DisposeAsync().AsTask().ConfigureAwait(false); }
        catch (Exception ex)
        { Debug.WriteLine($"[ChatVM] MessageManager dispose error: {ex.Message}"); }
        Context.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Interlocked.CompareExchange(ref _disposeStartedFlag, 1, 0) != 0) return;
            DisposeCommonResources();
            _ = MessageManager.DisposeAsync().AsTask().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Debug.WriteLine($"[ChatVM] MessageManager async dispose error: {t.Exception}");
            });
            Context.Dispose();
        }
        base.Dispose(disposing);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeStartedFlag, 1, 0) != 0) return;
        DisposeCommonResources();
        await MessageManager.DisposeAsync();
        Context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DisposeCommonResources()
    {
        _refreshDebounce?.Cancel();
        _refreshDebounce?.Dispose();
        _refreshDebounce = null;

        _hubSubscriber.Dispose();
        Context.Hub.SetCurrentChat(null);
        Context.Hub.ChatUpdated -= OnChatUpdated;

        Composer.Dispose();
        Permissions.Dispose();
        Sections.Dispose();

        Call.Dispose();
        EditDelete.Dispose();
        Reply.Dispose();
        Forward.Dispose();
        Typing.Dispose();
        Voice.Dispose();
        InfoPanel.SetPolls([]);
        InfoPanel.SetPhotos([]);
        InfoPanel.Dispose();
        Search.Dispose();
        Notification.Dispose();
        Pinned.Dispose();
        Attachments.Dispose();

        MembersPreView.Clear();
    }

    #endregion
}