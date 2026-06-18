using Avalonia.Input;
using Core.Infrastructure.Media;
using Core.Services.Features.Media.Files;
using Core.ViewModels.Chat.Commands;
using Core.ViewModels.Chat.Composer;
using Core.ViewModels.Chat.Context;
using Core.ViewModels.Chat.Core;
using Core.ViewModels.Chat.Features.Call;
using Core.ViewModels.Chat.Features.Media;
using Core.ViewModels.Chat.Features.Pinned;
using Core.ViewModels.Chat.Managers;
using Core.ViewModels.Chat.Navigation;
using Core.ViewModels.Chat.Permissions;
using Core.ViewModels.Chat.Relay;
using Core.ViewModels.Chat.Scroll;
using Core.ViewModels.ChatList.Factories;
using Core.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Core.ViewModels.Chat;

public sealed partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    public enum InfoSectionType { None, Photos, Files, Polls, Members, Pinned }

    #region Дочерние компоненты

    public ChatContext Context { get; }
    public ChatsViewModel Parent { get; }
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
    private readonly ChatPropertyRelay _relay;
    private readonly TaskCompletionSource _initTcs = new();
    private readonly int? _targetMessageId;
    private readonly IFileDownloadService _fileDownloadService;
    private readonly IFileDownloadStateService? _fileDownloadStateService;
    private readonly INotificationService _notificationService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IAudioRecorderService _audioRecorderService;

    #endregion

    #region Observable свойства

    public string NewMessage
    {
        get => Composer.NewMessage;
        set
        {
            if (Composer.NewMessage != value)
            {
                Composer.NewMessage = value;
                OnPropertyChanged(nameof(NewMessage));
            }
        }
    }

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

    #region Делегированные свойства (Composer)

    public bool CanSendMessageNow => Composer.CanSendMessageNow;
    public bool ShowSendMessageButton => Composer.ShowSendMessageButton;
    public bool ShowVoiceMessageButton => Composer.ShowVoiceMessageButton;
    public bool IsMultiLine => Composer.IsMultiLine;
    public bool IsMentionSuggestionsOpen
    {
        get => Composer.IsMentionSuggestionsOpen;
        set => Composer.IsMentionSuggestionsOpen = value;
    }
    public int MentionSelectedIndex
    {
        get => Composer.MentionSelectedIndex;
        set => Composer.MentionSelectedIndex = value;
    }
    public ObservableCollection<UserDto> MentionSuggestions => Composer.MentionSuggestions;
    public List<string> PopularEmojis => Composer.PopularEmojis;

    #endregion

    #region Делегированные свойства (Sections)

    public int PhotosCount => Sections.PhotosCount;
    public int FilesCount => Sections.FilesCount;
    public int PollsCount => Sections.PollsCount;
    public bool HasPhotos => Sections.HasPhotos;
    public bool HasFiles => Sections.HasFiles;
    public bool HasPolls => Sections.HasPolls;
    public ObservableCollection<ChatInfoPanelMediaItem> PhotosItems => Sections.PhotosItems;
    public ObservableCollection<ChatInfoPanelFileItem> FilesItems => Sections.FilesItems;
    public ObservableCollection<MessageViewModel> PollMessages => Sections.PollMessages;

    #endregion

    #region Делегированные свойства (Permissions)

    public bool CanEditGroupChat => Permissions.CanEditGroupChat;
    public bool CanLeaveChat => Permissions.CanLeaveChat;
    public bool CanLeaveChatVisible => Permissions.CanLeaveChatVisible;

    #endregion

    #region Делегированные свойства (InfoPanel)

    public bool HasMorePhotos => InfoPanel.HasMorePhotos;
    public string RemainingPhotosText => InfoPanel.RemainingPhotosText;
    public string MemberSearchQuery
    {
        get => InfoPanel.MemberSearchQuery;
        set => InfoPanel.MemberSearchQuery = value;
    }
    public string PollSearchQuery
    {
        get => InfoPanel.PollSearchQuery;
        set => InfoPanel.PollSearchQuery = value;
    }
    public ObservableCollection<MessageViewModel> FilteredPolls => InfoPanel.FilteredPolls;
    public ObservableCollection<UserDto> FilteredMembers => InfoPanel.FilteredMembers;
    public string InfoPanelTitle => InfoPanel.InfoPanelTitle;
    public string InfoPanelSubtitle => InfoPanel.InfoPanelSubtitle;
    public string? ContactAvatar => InfoPanel.ContactAvatar;
    public string? ContactDisplayName => InfoPanel.ContactDisplayName;
    public string? ContactUsername => InfoPanel.ContactUsername;
    public string? ContactDepartment => InfoPanel.ContactDepartment;
    public string? ContactLastSeen => InfoPanel.ContactLastSeen;
    public bool IsContactOnline => InfoPanel.IsContactOnline;
    public bool IsGroupChat => InfoPanel.IsGroupChat;
    public bool IsContactChat => InfoPanel.IsContactChat;
    public bool IsDepartmentChat => Chat?.Type == ChatType.Department;
    public bool IsDepartmentHeadsChat => Chat?.Type == ChatType.DepartmentHeads;
    public bool IsDepartmentScopedChat => IsDepartmentChat || IsDepartmentHeadsChat;

    #endregion

    #region Делегированные свойства (Pinned, Typing, etc.)

    public bool HasMultiplePinned => Pinned.HasMultiplePinned;
    public int PinnedCount => Pinned.PinnedCount;
    public string TypingText => Typing.TypingText;
    public bool IsEditMode => EditDelete.IsEditMode;
    public bool IsReplyMode => Reply.IsReplyMode;
    public bool IsForwardMode => Forward.IsForwardMode;
    public bool IsVoiceRecording => Voice.IsVoiceRecording;
    public bool IsLoadingMuteState => Notification.IsLoadingMuteState;
    public bool HasMoreNewer => MessageManager.HasMoreNewer;
    public bool HasMoreOlder => MessageManager.HasMoreOlder;
    public bool ShowScrollToBottom => !IsScrolledToBottom;

    #endregion

    #region Вычисляемые свойства

    public bool HasContactInfoMediaSection =>
        HasPhotos || HasFiles || Pinned.HasPinnedMessages;

    public bool HasGroupInfoMediaSection =>
        HasPhotos || HasFiles || Pinned.HasPinnedMessages || HasPolls;

    public bool ShowPinnedSection => CurrentInfoSection == InfoSectionType.Pinned;
    public bool ShowPhotosSection => CurrentInfoSection == InfoSectionType.Photos;
    public bool ShowFilesSection => CurrentInfoSection == InfoSectionType.Files;
    public bool ShowPollsSection => CurrentInfoSection == InfoSectionType.Polls;
    public bool ShowMembersSection => CurrentInfoSection == InfoSectionType.Members;

    public string MemberCountText
    {
        get
        {
            if (Chat?.Type == ChatType.Contact)
            {
                if (IsContactOnline) return "в сети";
                return ContactLastSeen ?? string.Empty;
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

    #endregion

    #region Проксированные коллекции

    public ObservableCollection<MessageViewModel> Messages => MessageManager.Messages;
    public ObservableCollection<LocalFileAttachment> LocalAttachments => Attachments.Attachments;
    public ObservableCollection<UserDto> Members => Context.Members;
    public ObservableCollection<UserDto> MembersPreview { get; } = [];

    public ChatDto? Chat
    {
        get => Context.Chat;
        set => Context.Chat = value;
    }

    public bool IsSearchMode
    {
        get => Search.IsSearchMode;
        set => Search.IsSearchMode = value;
    }

    public bool IsInfoPanelOpen
    {
        get => InfoPanel.IsInfoPanelOpen;
        set
        {
            if (InfoPanel.IsInfoPanelOpen == value) return;
            InfoPanel.IsInfoPanelOpen = value;
            if (value) RefreshInfoPanelLists();
            else OpenInfoSection(InfoSectionType.None);
        }
    }

    public bool IsChatNotificationsEnabled
    {
        get => Notification.IsNotificationEnabled;
        set
        {
            if (Notification.IsNotificationEnabled == value) return;
            _ = Notification.ToggleCommand.ExecuteAsync(null);
        }
    }

    public bool HasInitialMessageTarget => _targetMessageId.HasValue;
    public Action? RequestGoBackToList { get; set; }

    #endregion

    #region Events

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    #endregion

    #region Constructor

    public ChatViewModel(ChatDto initialChat, ChatsViewModel parent, IChatNavigator navigator,
        ChatViewModelDependencies dependencies, int? targetMessageId = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(initialChat);

        _fileDownloadService = dependencies.FileDownloadService;
        _fileDownloadStateService = dependencies.FileDownloadStateService;
        _notificationService = dependencies.NotificationService;
        _audioPlayerService = dependencies.AudioPlayer;
        _audioRecorderService = dependencies.AudioRecorder;

        var currentUserId = dependencies.AuthManager.Session.UserId
            ?? throw new InvalidOperationException("Пользователь не авторизован");

        UserId = currentUserId;
        _targetMessageId = targetMessageId;

        Context = new ChatContext(initialChat.Id, currentUserId, dependencies.Core, dependencies.Media, dependencies.Cache)
        {
            Chat = initialChat,
            Navigator = navigator,
            IsSystemAdmin = dependencies.AuthManager.Session.IsAdmin
        };

        Context.ScrollToMessageRequested += (msg, hl) => ScrollToMessageRequested?.Invoke(msg, hl);
        Context.ScrollToIndexRequested += (idx, hl) => ScrollToIndexRequested?.Invoke(idx, hl);
        Context.ScrollToBottomRequested += () => ScrollToBottomRequested?.Invoke();
        Context.RequestRefreshCounters = () => _ = Sections.RefreshCountsAsync();

        dependencies.GlobalHub.SetCurrentChat(initialChat.Id);

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

        MessageManager = new ChatMessageManager(Context, dependencies.Media, chatCommands, OpenMentionProfileCommand);
        Attachments = new ChatAttachmentManager(initialChat.Id, dependencies.ApiClient, dependencies.PlatformService);
        MemberLoader = new ChatMemberLoader(initialChat.Id, currentUserId, dependencies.ApiClient);

        EditDelete = new ChatEditDeleteHandler(Context);
        Reply = new ChatReplyHandler(Context, MessageManager);
        Forward = new ChatForwardHandler(Context);
        Typing = new ChatTypingHandler(Context);
        Voice = new ChatVoiceHandler(Context, () => Reply.CancelReply());
        InfoPanel = new ChatInfoPanelHandler(Context, dependencies.ChatInfoPanelStateStore, MemberLoader, dependencies.PlatformService);
        Search = new ChatSearchHandler(Context, MessageManager);
        Notification = new ChatNotificationHandler(Context);

        Pinned = new ChatPinnedHandler(Context, dependencies.FileDownloadService, dependencies.FileDownloadStateService,
            dependencies.AudioPlayer, dependencies.NotificationService);

        Call = new ChatCallHandler(Context, dependencies.CallService, dependencies.CallHub,
            openCallUi: () => { Parent.OpenCallUi(); return Task.CompletedTask; },
            showCallView: (state, name, isGroup) =>
            {
                Parent.ShowCallView(state, name, isGroup);
                return Task.CompletedTask;
            });

        Composer = new ChatComposer(Context, Attachments, Reply, Forward, EditDelete, MessageManager);
        Composer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatComposer.NewMessage))
            {
                Typing.NotifyTextChanged(Composer.NewMessage);
                OnPropertyChanged(nameof(NewMessage));
                OnPropertyChanged(nameof(IsMultiLine));
            }
        };
        Composer.ComposerStateChanged += () =>
        {
            OnPropertyChanged(nameof(CanSendMessageNow));
            OnPropertyChanged(nameof(ShowSendMessageButton));
            OnPropertyChanged(nameof(ShowVoiceMessageButton));
            OnPropertyChanged(nameof(IsMultiLine));
        };

        Scroll = new ChatScrollCoordinator(Context, MessageManager);
        Permissions = new ChatPermissionsManager(Context, dependencies.DialogService);
        Sections = new ChatSectionManager(Context, InfoPanel, MessageManager, _fileDownloadService, _fileDownloadStateService,
            _notificationService, currentUserId);

        chatCommands.Edit = EditDelete.StartEditCommand;
        chatCommands.Copy = EditDelete.CopyMessageTextCommand;
        chatCommands.Delete = EditDelete.DeleteMessageCommand;
        chatCommands.TogglePin = EditDelete.TogglePinCommand;
        chatCommands.Reply = Reply.StartReplyCommand;
        chatCommands.ScrollToReply = Reply.ScrollToReplyOriginalCommand;
        chatCommands.Forward = Forward.StartForwardCommand;

        _relay = BuildPropertyRelay();

        _hubSubscriber = new ChatHubSubscriber(Context, MessageManager, count => UnreadCount = count, OnHubReconnectedAsync);
        _hubSubscriber.Subscribe();

        Context.Hub.ChatUpdated += OnChatUpdated;
        Context.Hub.UserRoleUpdated += OnUserRoleUpdated;

        _ = InitializeAsync();
    }

    #endregion

    #region Property Relay

    private ChatPropertyRelay BuildPropertyRelay()
    {
        var relay = new ChatPropertyRelay(OnPropertyChanged);

        relay.Forward(Composer,
            (nameof(ChatComposer.IsMentionSuggestionsOpen), nameof(IsMentionSuggestionsOpen)),
            (nameof(ChatComposer.MentionSelectedIndex), nameof(MentionSelectedIndex)));

        relay.Forward(Sections,
            (nameof(ChatSectionManager.PhotosCount), nameof(PhotosCount)),
            (nameof(ChatSectionManager.FilesCount), nameof(FilesCount)),
            (nameof(ChatSectionManager.PollsCount), nameof(PollsCount)),
            (nameof(ChatSectionManager.HasPhotos), nameof(HasPhotos)),
            (nameof(ChatSectionManager.HasFiles), nameof(HasFiles)),
            (nameof(ChatSectionManager.HasPolls), nameof(HasPolls)));

        relay.Forward(Permissions,
            (nameof(ChatPermissionsManager.CanEditGroupChat), nameof(CanEditGroupChat)),
            (nameof(ChatPermissionsManager.CanLeaveChat), nameof(CanLeaveChat)),
            (nameof(ChatPermissionsManager.CanLeaveChatVisible), nameof(CanLeaveChatVisible)));

        relay.Forward(InfoPanel,
            (nameof(ChatInfoPanelHandler.IsInfoPanelOpen), nameof(IsInfoPanelOpen)),
            (nameof(ChatInfoPanelHandler.IsGroupChat), nameof(IsGroupChat)),
            (nameof(ChatInfoPanelHandler.IsContactChat), nameof(IsContactChat)),
            (nameof(ChatInfoPanelHandler.InfoPanelTitle), nameof(InfoPanelTitle)),
            (nameof(ChatInfoPanelHandler.InfoPanelSubtitle), nameof(InfoPanelSubtitle)),
            (nameof(ChatInfoPanelHandler.ContactAvatar), nameof(ContactAvatar)),
            (nameof(ChatInfoPanelHandler.ContactDisplayName), nameof(ContactDisplayName)),
            (nameof(ChatInfoPanelHandler.ContactUsername), nameof(ContactUsername)),
            (nameof(ChatInfoPanelHandler.ContactDepartment), nameof(ContactDepartment)),
            (nameof(ChatInfoPanelHandler.ContactLastSeen), nameof(ContactLastSeen)),
            (nameof(ChatInfoPanelHandler.IsContactOnline), nameof(IsContactOnline)),
            (nameof(ChatInfoPanelHandler.MemberSearchQuery), nameof(MemberSearchQuery)),
            (nameof(ChatInfoPanelHandler.PollSearchQuery), nameof(PollSearchQuery)),
            (nameof(ChatInfoPanelHandler.HasMorePhotos), nameof(HasMorePhotos)),
            (nameof(ChatInfoPanelHandler.RemainingPhotosText), nameof(RemainingPhotosText)),
            (nameof(ChatInfoPanelHandler.IsGroupChat), nameof(CanLeaveChatVisible)));

        relay.Forward(Pinned,
            (nameof(ChatPinnedHandler.HasPinnedMessages), nameof(HasContactInfoMediaSection)),
            (nameof(ChatPinnedHandler.HasPinnedMessages), nameof(HasGroupInfoMediaSection)),
            (nameof(ChatPinnedHandler.PinnedCount), nameof(PinnedCount)),
            (nameof(ChatPinnedHandler.HasMultiplePinned), nameof(HasMultiplePinned)));

        relay.Forward(Typing,
            (nameof(ChatTypingHandler.TypingText), nameof(TypingText)));

        relay.Forward(EditDelete,
            (nameof(ChatEditDeleteHandler.IsEditMode), nameof(IsEditMode)),
            (nameof(ChatEditDeleteHandler.IsEditMode), nameof(ShowSendMessageButton)),
            (nameof(ChatEditDeleteHandler.IsEditMode), nameof(ShowVoiceMessageButton)));

        relay.Forward(Reply,
            (nameof(ChatReplyHandler.IsReplyMode), nameof(IsReplyMode)));

        relay.Forward(Forward,
            (nameof(ChatForwardHandler.IsForwardMode), nameof(IsForwardMode)),
            (nameof(ChatForwardHandler.ForwardingMessage), nameof(CanSendMessageNow)),
            (nameof(ChatForwardHandler.ForwardingMessage), nameof(ShowSendMessageButton)));

        relay.Forward(Search,
            (nameof(ChatSearchHandler.IsSearchMode), nameof(IsSearchMode)));

        relay.Forward(Voice,
            (nameof(ChatVoiceHandler.IsVoiceRecording), nameof(IsVoiceRecording)));

        relay.Forward(Notification,
            (nameof(ChatNotificationHandler.IsLoadingMuteState), nameof(IsLoadingMuteState)),
            (nameof(ChatNotificationHandler.IsNotificationEnabled), nameof(IsChatNotificationsEnabled)));

        relay.Forward(Context,
            (nameof(ChatContext.Chat), nameof(Chat)),
            (nameof(ChatContext.Members), nameof(Members)),
            (nameof(ChatContext.Members), nameof(InfoPanelSubtitle)),
            (nameof(ChatContext.Members), nameof(MemberCountText)));

        relay.Forward(Call,
            (nameof(ChatCallHandler.HasActiveCall), nameof(ChatCallHandler.HasActiveCall)),
            (nameof(ChatCallHandler.ActiveCallParticipantsCount), nameof(ChatCallHandler.ActiveCallParticipantsCount)),
            (nameof(ChatCallHandler.ActiveCallBannerText), nameof(ChatCallHandler.ActiveCallBannerText)),
            (nameof(ChatCallHandler.IsInActiveCall), nameof(ChatCallHandler.IsInActiveCall)));

        relay.ForwardCollection(Context.Members, () =>
        {
            ScheduleRefreshInfoPanelLists();
            OnPropertyChanged(nameof(MemberCountText));
        });

        relay.ForwardCollection(MessageManager.Messages, ScheduleRefreshInfoPanelLists);

        relay.ForwardCollection(InfoPanel.FilteredMembers, nameof(FilteredMembers));

        relay.ForwardCollection(InfoPanel.FilteredPolls, nameof(FilteredPolls));

        relay.ForwardCollection(Attachments.Attachments, () =>
        {
            OnPropertyChanged(nameof(CanSendMessageNow));
            OnPropertyChanged(nameof(ShowSendMessageButton));
            OnPropertyChanged(nameof(ShowVoiceMessageButton));
        });

        return relay;
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
            var countsTask = Context.Api.GetAsync<ChatCountsDto>(ApiEndpoints.Messages.Counts(Context.ChatId), Context.LifetimeToken);

            await Task.WhenAll(membersTask, pinnedTask, countsTask);

            var countsResult = countsTask.Result;
            if (countsResult is { Success: true, Data: not null })
            {
                Sections.ApplyCounts(countsResult.Data);
            }
            else
            {
                Debug.WriteLine($"[ChatVM] Не удалось загрузить счётчики: {countsResult?.Error}");
            }

            Context.Members = membersTask.Result;
            Permissions.Refresh();

            if (InfoPanel.IsContactChat)
                await InfoPanel.LoadContactUserAsync();

            OnPropertyChanged(nameof(InfoPanel));

            var scrollToIndex = await MessageManager.LoadInitialMessagesAsync(_targetMessageId, Context.LifetimeToken);

            if (_targetMessageId.HasValue && scrollToIndex.HasValue)
                Context.RequestScrollToIndex(scrollToIndex.Value, highlight: true);
            else if (scrollToIndex.HasValue && scrollToIndex < Messages.Count - 1)
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
            Debug.WriteLine($"[ChatVM] StackTrace: {ex.StackTrace}");
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
        Debug.WriteLine($"[VM] IsScrolledToBottom={value} → ShowScrollToBottom={!value}");
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
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading || !MessageManager.HasMoreNewer) return;
        IsLoadingNewerMessages = true;
        try { await MessageManager.LoadNewerMessagesAsync(Context.LifetimeToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[ChatVM] LoadNewerMessages error: {ex.Message}"); }
    }

    [RelayCommand]
    private void ToggleInfoPanel() => IsInfoPanelOpen = !IsInfoPanelOpen;

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
        if (!IsInfoPanelOpen) IsInfoPanelOpen = true;
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
    private void LoadMorePhotos()
    {
        InfoPanel.LoadMorePhotos();
        OnPropertyChanged(nameof(PhotosCount));
        OnPropertyChanged(nameof(PhotosItems));
    }

    [RelayCommand]
    private Task StartOrJoinCallAsync() => Call.StartOrJoinCallCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task OpenCreatePoll()
        => await _navigator.ShowPollDialogAsync(Context.ChatId, () => MessageManager.LoadInitialMessagesAsync(null));

    [RelayCommand]
    private async Task OpenEditChat()
    {
        if (!InfoPanel.IsGroupChat || Context.Chat == null || !CanEditGroupChat) return;

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

        var user = Members.FirstOrDefault(m => string.Equals(m.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

        if (user?.Id > 0)
            await _navigator.ShowUserProfileAsync(user.Id);
    }

    [RelayCommand]
    private async Task LeaveChat()
    {
        if (!CanLeaveChat)
        {
            ErrorMessage = "Владелец чата не может покинуть чат";
            return;
        }

        var confirmDialog = new ConfirmDialogViewModel("Покинуть группу", "Вы уверены, что хотите покинуть группу?", "Покинуть", "Отмена");

        await Parent.Parent.ShowDialogAsync(confirmDialog);
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
            RebuildMembersPreview();
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
                RebuildMembersPreview();
                InfoPanel.OnMembersSectionOpened();
                break;
        }
    }

    private void RebuildMembersPreview()
    {
        MembersPreview.Clear();
        foreach (var member in Context.Members.OrderByDescending(m => m.IsOnline)
            .ThenBy(m => m.DisplayName ?? m.Username ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(5))
        {
            MembersPreview.Add(member);
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

    private void OnUserRoleUpdated(UserRole role) => Dispatcher.UIThread.Post(() =>
    {
        if (Context.IsDisposed) return;
        Context.IsSystemAdmin = role.HasFlag(UserRole.Admin);
        Permissions.Refresh();
    });

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
                var oldRole = Context.CurrentUserRole;
                Context.CurrentUserRole = update.CurrentUserRole.Value;
                Permissions.Refresh();
            }

            if (oldAvatar != update.Avatar)
            {
                if (!string.IsNullOrEmpty(oldAvatar))
                {
                    try
                    {
                        AppConfig.Services
                            .GetRequiredService<AuthenticatedImageLoader>()
                            .InvalidateByRelativePath(oldAvatar);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatVM] Avatar invalidate error: {ex.Message}");
                    }
                }
                Context.Chat ??= new ChatDto();
                Context.Chat.Avatar = update.Avatar;
            }

            OnPropertyChanged(nameof(Chat));
            OnPropertyChanged(nameof(CanEditGroupChat));
            OnPropertyChanged(nameof(CanLeaveChat));
            OnPropertyChanged(nameof(CanLeaveChatVisible));
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
            await Task.WhenAll(MessageManager.GapFillAfterReconnectAsync(ct), RefreshInfoPanelAsync(ct));
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
            var chatResult = await Context.Api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(Context.ChatId), ct);

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
        RebuildMembersPreview();
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
        catch (Exception ex) { Debug.WriteLine($"[ChatVM] MessageManager dispose error: {ex.Message}"); }
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

        _relay.Dispose();
        _hubSubscriber.Dispose();
        Context.Hub.SetCurrentChat(null);
        Context.Hub.ChatUpdated -= OnChatUpdated;
        Context.Hub.UserRoleUpdated -= OnUserRoleUpdated;

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

        MembersPreview.Clear();
        PhotosItems.Clear();
        FilesItems.Clear();
    }

    #endregion
}