using Avalonia.Input;
using Avalonia.Platform.Storage;
using Desktop.Infrastructure.Helpers;
using Desktop.Infrastructure.Media;
using Desktop.Services.Features.Media.Files;
using Desktop.ViewModels.Chat.Commands;
using Desktop.ViewModels.Chat.Context;
using Desktop.ViewModels.Chat.Core;
using Desktop.ViewModels.Chat.Features.Media;
using Desktop.ViewModels.Chat.Managers;
using Desktop.ViewModels.Chat.Navigation;
using Desktop.ViewModels.ChatList.Factories;
using Desktop.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;
using Shared.Dto.Call;
using Shared.DTO.Call;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.ViewModels.Chat;

public sealed partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    public enum InfoSectionType { None, Photos, Files, Polls, Members, Pinned }
    private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _propertyForwardingHandlers = [];
    private NotifyCollectionChangedEventHandler? _membersCollectionHandler;
    private NotifyCollectionChangedEventHandler? _filteredMembersCollectionHandler;
    private NotifyCollectionChangedEventHandler? _filteredPollsCollectionHandler;
    private CancellationTokenSource? _refreshDebounce;
    private Action<MessageViewModel, bool>? _contextScrollToMessage;
    private Action<int, bool>? _contextScrollToIndex;
    private Action? _contextScrollToBottom;

    private static readonly Dictionary<InfoSectionType, string> InfoSectionTitles = new()
    {
        [InfoSectionType.Photos] = "Медиа",
        [InfoSectionType.Files] = "Документы",
        [InfoSectionType.Polls] = "Опросы",
        [InfoSectionType.Members] = "Участники",
        [InfoSectionType.Pinned] = "Закреплённые сообщения"
    };

    #region Зависимости и хэндлеры

    private readonly IFileDownloadService _fileDownloadService;
    private readonly IFileDownloadStateService? _fileDownloadStateService;
    private readonly INotificationService _notificationService;
    private readonly IAudioPlayerService _audioPlayerService;

    public ChatContext Context { get; }
    public ChatsViewModel Parent { get; }

    public ChatMessageManager MessageManager { get; }
    public ChatAttachmentManager Attachments { get; }
    public ChatMemberLoader MemberLoader { get; }
    public ChatEditDeleteHandler EditDelete { get; }
    public ChatReplyHandler Reply { get; }
    public ChatForwardHandler Forward { get; }
    public ChatTypingHandler Typing { get; }
    public ChatVoiceHandler Voice { get; }
    public ChatInfoPanelHandler InfoPanel { get; }
    public ChatSearchHandler Search { get; }
    public ChatNotificationHandler Notification { get; }

    private readonly IChatNavigator _navigator;
    private readonly ChatHubSubscriber _hubSubscriber;
    private readonly TaskCompletionSource _initTcs = new();
    private DateTime _lastMarkAsReadTime = DateTime.MinValue;
    private int _composerCaretIndex;
    private readonly ICallService _callService;
    private readonly ICallHubConnection _callHub;
    private readonly IAudioRecorderService _audioRecorderService;
    private readonly bool _isSystemAdmin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveCallBannerText))]
    public partial bool HasActiveCall { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveCallBannerText))]
    public partial int ActiveCallParticipantsCount { get; set; }

    [ObservableProperty]
    public partial bool IsInActiveCall { get; set; }

    public string ActiveCallBannerText => ActiveCallParticipantsCount > 0 ? $"Идёт звонок · {ActiveCallParticipantsCount} участн." : "Идёт звонок";

    #endregion

    #region Проксированные коллекции и свойства
    [RelayCommand]
    public void LoadMorePhotos() => InfoPanel.LoadMorePhotos();
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
    public ObservableCollection<MessageViewModel> Messages => MessageManager.Messages;
    public ObservableCollection<LocalFileAttachment> LocalAttachments => Attachments.Attachments;
    public ObservableCollection<UserDto> Members => Context.Members;

    public ObservableCollection<UserDto> MembersPreview { get; } = [];
    public ObservableCollection<ChatInfoPanelMediaItem> PhotosItems { get; } = [];
    public ObservableCollection<ChatInfoPanelFileItem> FilesItems { get; } = [];
    public ObservableCollection<MessageViewModel> PollMessages { get; } = [];
    public ObservableCollection<UserDto> MentionSuggestions { get; } = [];

    public string InfoPanelTitle => InfoPanel.InfoPanelTitle;
    public string InfoPanelSubtitle => InfoPanel.InfoPanelSubtitle;
    public string? ContactAvatar => InfoPanel.ContactAvatar;
    public string? ContactDisplayName => InfoPanel.ContactDisplayName;
    public string? ContactUsername => InfoPanel.ContactUsername;
    public string? ContactDepartment => InfoPanel.ContactDepartment;
    public string? ContactLastSeen => InfoPanel.ContactLastSeen;
    public bool IsContactOnline => InfoPanel.IsContactOnline;
    public bool IsGroupChat => InfoPanel.IsGroupChat;
    public bool IsDepartmentChat => Chat?.Type == ChatType.Department;
    public bool IsDepartmentHeadsChat => Chat?.Type == ChatType.DepartmentHeads;
    public bool IsDepartmentScopedChat => IsDepartmentChat || IsDepartmentHeadsChat;
    public bool CanEditGroupChat { get; private set; }
    public bool CanLeaveChat { get; private set; } = true;
    public bool IsContactChat => InfoPanel.IsContactChat;
    public bool HasMultiplePinned => PinnedMessages.Count > 1;
    public string TypingText => Typing.TypingText;
    public bool IsEditMode => EditDelete.IsEditMode;
    public bool IsReplyMode => Reply.IsReplyMode;
    public bool IsForwardMode => Forward.IsForwardMode;
    public bool IsVoiceRecording => Voice.IsVoiceRecording;
    public bool IsLoadingMuteState => Notification.IsLoadingMuteState;
    public bool HasMoreNewer => MessageManager.HasMoreNewer;
    public bool ShowScrollToBottom => !IsScrolledToBottom;
    public bool IsMultiLine => !string.IsNullOrEmpty(NewMessage) && NewMessage.Contains('\n');

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
        set => InfoPanel.IsInfoPanelOpen = value;
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

    #endregion

    #region Observable properties

    public ObservableCollection<MessageViewModel> PinnedMessages { get; } = [];
    public int PinnedCount => PinnedMessages.Count;

    public bool ShowPinnedSection => CurrentInfoSection == InfoSectionType.Pinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinnedBannerVisible))]
    [NotifyPropertyChangedFor(nameof(PinnedBannerPreviewText))]
    public partial MessageViewModel? PinnedBannerMessage { get; set; }

    public bool IsPinnedBannerVisible => PinnedBannerMessage != null;

    [ObservableProperty] public partial string NewMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsMentionSuggestionsOpen { get; set; }
    [ObservableProperty] public partial int MentionSelectedIndex { get; set; } = -1;
    [ObservableProperty] public partial bool IsInitialLoading { get; set; } = true;
    [ObservableProperty] public partial bool IsLoadingOlderMessages { get; set; }
    [ObservableProperty] public partial bool HasNewMessages { get; set; }
    [ObservableProperty] public partial bool IsScrolledToBottom { get; set; } = true;
    [ObservableProperty] public partial int UnreadCount { get; set; }
    [ObservableProperty] public partial int PollsCount { get; set; }
    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial UserProfileDialogViewModel? UserProfileDialog { get; set; }
    [ObservableProperty] public partial bool IsInfoSectionOpen { get; set; }
    [ObservableProperty] public partial string InfoSectionTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoSectionType CurrentInfoSection { get; set; }

    public bool ShowPhotosSection => CurrentInfoSection == InfoSectionType.Photos;
    public bool ShowFilesSection => CurrentInfoSection == InfoSectionType.Files;
    public bool ShowPollsSection => CurrentInfoSection == InfoSectionType.Polls;
    public bool ShowMembersSection => CurrentInfoSection == InfoSectionType.Members;
    public int PhotosCount => PhotosItems.Count;
    public int FilesCount => FilesItems.Count;

    public List<string> PopularEmojis { get; } =
    [
        "😀","😂","😍","🥰","😊","😎","🤔","😅","😭","😤",
        "❤","👍","👎","🎉","🔥","✨","💯","🙏","👏","🤝",
        "💪","🎁","📱","💻","🎮","🎵","📷","🌟","⭐","🌈","☀️","🌙"
    ];

    #endregion

    #region Events

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    #endregion
    partial void OnPinnedBannerMessageChanged(MessageViewModel? oldValue, MessageViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnPinnedBannerMessagePropertyChanged;

        if (newValue != null)
            newValue.PropertyChanged += OnPinnedBannerMessagePropertyChanged;
    }

    private void OnPinnedBannerMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MessageViewModel.ContentPreview)
            or nameof(MessageViewModel.SenderName)
            or nameof(MessageViewModel.Content))
        {
            OnPropertyChanged(nameof(PinnedBannerPreviewText));
        }
    }
    public string PinnedBannerPreviewText
    {
        get
        {
            var msg = PinnedBannerMessage;
            if (msg == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(msg.SenderName))
                return msg.ContentPreview;
            return $"{msg.SenderName}: {msg.ContentPreview}";
        }
    }
    #region Init

    public ChatViewModel(ChatDto initialChat, ChatsViewModel parent, IChatNavigator navigator, ChatViewModelDependencies dependencies,
        IStorageProvider? storageProvider = null)
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
        _isSystemAdmin = dependencies.AuthManager.Session.IsAdmin;

        var currentUserId = dependencies.AuthManager.Session.UserId ?? throw new InvalidOperationException("Пользователь не авторизован");

        UserId = currentUserId;

        var chatId = initialChat.Id;

        Context = new ChatContext(chatId, currentUserId, dependencies.Core, dependencies.Media, dependencies.Cache)
        {
            Chat = initialChat,
            Navigator = navigator
        };

        _contextScrollToMessage = (msg, hl) => ScrollToMessageRequested?.Invoke(msg, hl);
        _contextScrollToIndex = (idx, hl) => ScrollToIndexRequested?.Invoke(idx, hl);
        _contextScrollToBottom = () => ScrollToBottomRequested?.Invoke();

        Context.ScrollToMessageRequested += _contextScrollToMessage;
        Context.ScrollToIndexRequested += _contextScrollToIndex;
        Context.ScrollToBottomRequested += _contextScrollToBottom;

        dependencies.GlobalHub.SetCurrentChat(chatId);

        var chatCommands = new ChatCommands
        {
            OpenProfile = OpenProfileCommand,
            ShowPollResults = new AsyncRelayCommand<PollViewModel>(async vm =>
            {
                if (vm?.CurrentPollDto == null) return;
                var dialog = new PollResultsDialogViewModel(
                    vm.CurrentPollDto,
                    Context.Members,
                    Context.CurrentUserId,
                    Context.Api);
                await Context.Dialogs.ShowAsync(dialog);
                await dialog.TriggerInitializeAsync();
            })
        };

        MessageManager = new ChatMessageManager(Context, dependencies.Media, chatCommands, OpenMentionProfileCommand);

        Attachments = new ChatAttachmentManager(chatId, dependencies.ApiClient, storageProvider);
        MemberLoader = new ChatMemberLoader(chatId, currentUserId, dependencies.ApiClient);

        EditDelete = new ChatEditDeleteHandler(Context);
        Reply = new ChatReplyHandler(Context, MessageManager);
        Forward = new ChatForwardHandler(Context);
        Typing = new ChatTypingHandler(Context);
        Voice = new ChatVoiceHandler(Context, () => Reply.CancelReply());
        InfoPanel = new ChatInfoPanelHandler(Context, dependencies.ChatInfoPanelStateStore, MemberLoader, dependencies.PlatformService);
        Search = new ChatSearchHandler(Context, MessageManager);
        Notification = new ChatNotificationHandler(Context);

        chatCommands.Edit = EditDelete.StartEditCommand;
        chatCommands.Copy = EditDelete.CopyMessageTextCommand;
        chatCommands.Delete = EditDelete.DeleteMessageCommand;
        chatCommands.TogglePin = EditDelete.TogglePinCommand;
        chatCommands.Reply = Reply.StartReplyCommand;
        chatCommands.ScrollToReply = Reply.ScrollToReplyOriginalCommand;
        chatCommands.Forward = Forward.StartForwardCommand;

        _hubSubscriber = new ChatHubSubscriber(Context, MessageManager, count => UnreadCount = count, OnHubReconnectedAsync);
        _hubSubscriber.Subscribe();
        SubscribePropertyForwarding();
        Context.Hub.ChatUpdated += OnChatUpdated;

        _callService = dependencies.CallService;
        _callHub = dependencies.CallHub;

        SubscribeCallEvents();
        _ = InitializeAsync();
    }


    private async Task InitializeAsync()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            IsInitialLoading = true;
            Debug.WriteLine($"[ChatVM] Init start chat={Context.ChatId}");

            var chatTask = Context.Api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(Context.ChatId), Context.LifetimeToken);
            var readInfoTask = Context.Hub.GetReadInfoAsync(Context.ChatId);
            var notificationTask = Notification.LoadSettingsAsync(Context.LifetimeToken);

            await Task.WhenAll(chatTask, readInfoTask, notificationTask);

            var chatResult = chatTask.Result;
            if (chatResult is { Success: true, Data: not null })
            {
                if (!_chatMetaUpdatedExternally)
                {
                    Context.Chat = chatResult.Data;
                }
                else
                {
                    var serverData = chatResult.Data;
                    serverData.Avatar = Context.Chat?.Avatar ?? serverData.Avatar;
                    Context.Chat = serverData;
                    Debug.WriteLine($"[ChatVM] Merged server data with external avatar: '{serverData.Avatar}'");
                }
            }
            else
            {
                throw new HttpRequestException($"Не удалось загрузить чат: {chatResult.Error}");
            }

            CallStateDto? callState = null;
            try
            {
                if (_callHub.IsConnected)
                {
                    callState = await _callHub.GetCallStateAsync(Context.ChatId);
                }
                else
                {
                    try
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(3);
                        while (!_callHub.IsConnected && DateTime.UtcNow < deadline)
                            await Task.Delay(100, Context.LifetimeToken);

                        if (_callHub.IsConnected)
                            callState = await _callHub.GetCallStateAsync(Context.ChatId);
                    }
                    catch (OperationCanceledException) { /* отменено */ }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatVM] GetCallState failed ({ex.GetType().Name}): {ex.Message}");
            }

            if (callState != null)
            {
                _chatActiveCallId = callState.CallId;
                HasActiveCall = true;
                ActiveCallParticipantsCount = callState.Participants.Count;
                IsInActiveCall = _callService.ActiveChatId == Context.ChatId;
            }

            MessageManager.SetReadInfo(readInfoTask.Result);

            var membersTask = MemberLoader.LoadMembersAsync(Context.Chat, Context.LifetimeToken);
            var pinnedTask = LoadPinnedAsync(Context.LifetimeToken);

            await Task.WhenAll(membersTask, pinnedTask);

            Context.Members = membersTask.Result;
            await RefreshChatPermissionsAsync();

            if (InfoPanel.IsContactChat)
                await InfoPanel.LoadContactUserAsync();

            OnPropertyChanged(nameof(InfoPanel));

            var scrollToIndex = await MessageManager.LoadInitialMessagesAsync(Context.LifetimeToken);

            if (scrollToIndex.HasValue && scrollToIndex < Messages.Count - 1)
            {
                Debug.WriteLine($"[ChatVM] Init chat={Context.ChatId} ScrollToIndex={scrollToIndex.Value}");
                Context.RequestScrollToIndex(scrollToIndex.Value);
            }
            else
            {
                Debug.WriteLine($"[ChatVM] Init chat={Context.ChatId} ScrollToBottom");
                Context.RequestScrollToBottom();
            }

            PollsCount = MessageManager.GetPollsCount();
            RefreshInfoPanelLists();

            var audioRecorder = _audioRecorderService;
            Voice.Initialize(audioRecorder);

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

    #region Проброс свойств (Property Forwarding)

    /// <summary>
    /// Подписывается на PropertyChanged источника и пробрасывает изменения
    /// в OnPropertyChanged этого ViewModel по таблице маппингов.
    /// </summary>
    private void ForwardProperties(INotifyPropertyChanged source, params (string sourceProp, string targetProp)[] mappings)
    {
        var lookup = mappings
            .GroupBy(m => m.sourceProp)
            .ToDictionary(g => g.Key, g => g.Select(x => x.targetProp).ToArray());

        void handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && lookup.TryGetValue(e.PropertyName, out var targets))
            {
                foreach (var target in targets)
                {
                    OnPropertyChanged(target);
                }
            }
        }

        source.PropertyChanged += handler;
        _propertyForwardingHandlers.Add((source, handler));
    }

    private void SubscribePropertyForwarding()
    {
        ForwardProperties(Typing, (nameof(ChatTypingHandler.TypingText), nameof(TypingText)));
        ForwardProperties(EditDelete, (nameof(ChatEditDeleteHandler.IsEditMode), nameof(IsEditMode)));
        ForwardProperties(Reply, (nameof(ChatReplyHandler.IsReplyMode), nameof(IsReplyMode)));
        ForwardProperties(Forward, (nameof(ChatForwardHandler.IsForwardMode), nameof(IsForwardMode)));
        ForwardProperties(Search, (nameof(ChatSearchHandler.IsSearchMode), nameof(IsSearchMode)));
        ForwardProperties(Voice, (nameof(ChatVoiceHandler.IsVoiceRecording), nameof(IsVoiceRecording)));

        ForwardProperties(InfoPanel,
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
            (nameof(ChatInfoPanelHandler.RemainingPhotosText), nameof(RemainingPhotosText)));

        ForwardProperties(Context,
            (nameof(ChatContext.Chat), nameof(Chat)),
            (nameof(ChatContext.Members), nameof(Members)),
            (nameof(ChatContext.Members), nameof(InfoPanelSubtitle)));

        ForwardProperties(Notification,
            (nameof(ChatNotificationHandler.IsLoadingMuteState), nameof(IsLoadingMuteState)),
            (nameof(ChatNotificationHandler.IsNotificationEnabled), nameof(IsChatNotificationsEnabled)));

        _membersCollectionHandler = (_, _) => ScheduleRefreshInfoPanelLists();
        Context.Members.CollectionChanged += _membersCollectionHandler;

        _filteredMembersCollectionHandler = (_, _) => OnPropertyChanged(nameof(FilteredMembers));
        InfoPanel.FilteredMembers.CollectionChanged += _filteredMembersCollectionHandler;

        _filteredPollsCollectionHandler = (_, _) => OnPropertyChanged(nameof(FilteredPolls));
        InfoPanel.FilteredPolls.CollectionChanged += _filteredPollsCollectionHandler;

        MessageManager.MessagePinStateChanged += OnMessagePinStateChanged;
        Context.MessagePinStateChanged += OnMessagePinStateChanged;
    }

    private void UnsubscribePropertyForwarding()
    {
        foreach (var (source, handler) in _propertyForwardingHandlers)
            source.PropertyChanged -= handler;
        _propertyForwardingHandlers.Clear();

        if (_membersCollectionHandler != null)
        {
            Context.Members.CollectionChanged -= _membersCollectionHandler;
            _membersCollectionHandler = null;
        }

        if (_filteredMembersCollectionHandler != null)
        {
            InfoPanel.FilteredMembers.CollectionChanged -= _filteredMembersCollectionHandler;
            _filteredMembersCollectionHandler = null;
        }

        if (_filteredPollsCollectionHandler != null)
        {
            InfoPanel.FilteredPolls.CollectionChanged -= _filteredPollsCollectionHandler;
            _filteredPollsCollectionHandler = null;
        }

        MessageManager.MessagePinStateChanged -= OnMessagePinStateChanged;
        Context.MessagePinStateChanged -= OnMessagePinStateChanged;
    }
    private void SubscribeCallEvents()
    {
        _callHub.ActiveCallStarted += OnActiveCallStarted;
        _callHub.ActiveCallUpdated += OnActiveCallUpdated;
        _callHub.ActiveCallEnded += OnActiveCallEnded;
        _callHub.IncomingCall += OnIncomingCall;

        _callService.CallStarted += OnCallStarted;
        _callService.CallEnded += OnCallEnded;
    }

    private void UnsubscribeCallEvents()
    {
        _callHub.ActiveCallStarted -= OnActiveCallStarted;
        _callHub.ActiveCallUpdated -= OnActiveCallUpdated;
        _callHub.ActiveCallEnded -= OnActiveCallEnded;
        _callHub.IncomingCall -= OnIncomingCall;

        _callService.CallStarted -= OnCallStarted;
        _callService.CallEnded -= OnCallEnded;
    }
    private string? _chatActiveCallId;

    private void OnActiveCallStarted(CallStateDto state)
    {
        if (state.ChatId != Context.ChatId) return;
        Dispatcher.UIThread.Post(() =>
        {
            _chatActiveCallId = state.CallId;
            HasActiveCall = true;
            ActiveCallParticipantsCount = state.Participants.Count;
        });
    }

    private void OnActiveCallUpdated(CallStateDto state)
    {
        if (state.ChatId != Context.ChatId) return;
        Dispatcher.UIThread.Post(() => ActiveCallParticipantsCount = state.Participants.Count);
    }

    private void OnActiveCallEnded(string callId) => Dispatcher.UIThread.Post(() =>
    {
        if (_chatActiveCallId != null && _chatActiveCallId != callId) return;
        _chatActiveCallId = null;
        HasActiveCall = false;
        ActiveCallParticipantsCount = 0;
        IsInActiveCall = false;
    });

    private void OnIncomingCall(CallInviteDto invite)
    {
        if (invite.ChatId != Context.ChatId) return;
        Dispatcher.UIThread.Post(() =>
        {
            _chatActiveCallId = invite.CallId;
            HasActiveCall = true;
            ActiveCallParticipantsCount = invite.ActiveParticipantsCount;
        });
    }

    private void OnCallStarted() => Dispatcher.UIThread.Post(() => IsInActiveCall = true);

    private void OnCallEnded() => Dispatcher.UIThread.Post(() => IsInActiveCall = false);
    #endregion

    #region Перехватчики изменений (Partial hooks)

    partial void OnNewMessageChanged(string value)
    {
        Typing.NotifyTextChanged(value);
        _composerCaretIndex = Math.Clamp(_composerCaretIndex, 0, value?.Length ?? 0);
        UpdateMentionSuggestions(value, _composerCaretIndex);
    }

    partial void OnIsScrolledToBottomChanged(bool value)
    {
        Debug.WriteLine($"[VM] IsScrolledToBottom={value} → ShowScrollToBottom={!value}");
        OnPropertyChanged(nameof(ShowScrollToBottom));
        if (!value) return;

        HasNewMessages = false;
        UnreadCount = 0;
        _ = MarkMessagesAsReadAsync();
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

    #region Helpers

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
            catch (OperationCanceledException) { /* Ожидаемая отмена */}
        });
    }

    private void RefreshInfoPanelLists()
    {
        if (Context.IsDisposed) return;

        PollMessages.Clear();

        PhotosItems.Clear();
        FilesItems.Clear();
        MembersPreview.Clear();

        var visible = MessageManager.Messages
            .Where(m => !m.IsDeleted && !m.IsSystemMessage)
            .ToList();

        var photos = visible
            .SelectMany(m => m.Files
                .Where(f => f.PreviewType == "image" && !string.IsNullOrWhiteSpace(f.Url))
                .Select(f => new ChatInfoPanelMediaItem(m, f)))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var files = visible
            .SelectMany(m => m.Files
                .Where(f => f.PreviewType != "image")
                .Select(f => new ChatInfoPanelFileItem(m, f)))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var polls = visible
            .Where(m => m.Poll != null)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        InfoPanel.SetPhotos(photos);

        foreach (var f in files)
            FilesItems.Add(f);
        foreach (var p in polls)
            PollMessages.Add(p);

        foreach (var m in Context.Members.Take(5))
            MembersPreview.Add(m);

        OnPropertyChanged(nameof(PhotosCount));
        OnPropertyChanged(nameof(FilesCount));
        InfoPanel.SetPolls(PollMessages);
    }

    #endregion

    [RelayCommand]
    private async Task StartOrJoinCallAsync()
    {
        if (Context.IsDisposed) return;

        await SafeExecuteAsync(async _ =>
        {
            if (_callService.IsInCall && _callService.ActiveChatId == Context.ChatId)
            {
                Parent.OpenCallUi();
                return;
            }

            if (_callService.IsInCall)
                await _callService.LeaveCallAsync();

            if (HasActiveCall)
            {
                var state = await _callHub.GetCallStateAsync(Context.ChatId);
                if (state == null) return;

                await _callService.JoinCallAsync(state.CallId, Context.ChatId);

                var chatName = Context.Chat?.Name ?? string.Empty;
                Parent.ShowCallView(state, chatName, state.IsGroupCall);
            }
            else
            {
                await _callService.StartCallAsync(Context.ChatId);
            }
        });
    }

    #region Messages

    private async Task LoadPinnedAsync(CancellationToken ct)
    {
        try
        {
            var result = await Context.Api.GetAsync<List<MessageDto>>(
                ApiEndpoints.Messages.PinnedForChat(Context.ChatId), ct);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Context.IsDisposed) return;

                var data = result is { Success: true, Data.Count: > 0 }
                    ? result.Data
                    : [];

                RebuildPinnedMessages(data);
            });
        }
        catch (OperationCanceledException) { /* Ожидаемая отмена */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Ошибка загрузки закреплённых: {ex.Message}");
        }
    }

    private void RebuildPinnedMessages(List<MessageDto> dtos)
    {
        PinnedBannerMessage?.Dispose();
        PinnedBannerMessage = null;

        foreach (var old in PinnedMessages) old.Dispose();
        PinnedMessages.Clear();

        foreach (var dto in dtos)
            PinnedMessages.Add(CreatePinnedMessageViewModel(dto));

        PinnedBannerMessage = PinnedMessages.Count > 0
            ? PinnedMessages[0]
            : null;

        OnPropertyChanged(nameof(PinnedCount));
        OnPropertyChanged(nameof(HasMultiplePinned));
    }


    private MessageViewModel CreatePinnedMessageViewModel(MessageDto dto)
    => new(dto, _fileDownloadService, _notificationService, _audioPlayerService, Context.Api,
           currentUserId: UserId, stateService: Context.FileDownloadState);

    [RelayCommand]
    private async Task CopyUsername()
        => await InfoPanel.CopyUsernameCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task SendMessage()
    {
        if (Context.IsDisposed) return;

        if (EditDelete.IsEditMode)
        {
            await EditDelete.SaveEditCommand.ExecuteAsync(null);
            return;
        }

        var forwarding = Forward.ForwardingMessage;
        var hasForward = forwarding != null;
        var hasText = !string.IsNullOrWhiteSpace(NewMessage);
        var hasAttachments = LocalAttachments.Count > 0;

        if (!hasText && !hasAttachments && !hasForward) return;

        await SafeExecuteAsync(async ct =>
        {
            var files = await Attachments.UploadAllAsync(ct);

            var content = NewMessage;
            if (hasForward && string.IsNullOrWhiteSpace(content))
                content = forwarding!.Content;

            var msg = new MessageDto
            {
                ChatId = Context.ChatId,
                Content = content,
                SenderId = Context.CurrentUserId,
                Files = files,
                ReplyToMessageId = Reply.ReplyingToMessage?.Id,
                ForwardedFromMessageId = forwarding?.Id
            };

            if (hasForward && files.Count == 0 && forwarding!.Files.Count > 0)
                msg.Files = forwarding.Files;

            var result = await Context.Api.PostAsync<MessageDto, MessageDto>(ApiEndpoints.Messages.Create, msg, ct);

            if (result.Success && result.Data != null)
            {
                NewMessage = string.Empty;
                Attachments.Clear();
                Reply.CancelReply();
                Forward.CancelForward();

                Dispatcher.UIThread.Post(() =>
                {
                    MessageManager.AddReceivedMessage(result.Data);
                    Context.RequestScrollToBottom();
                });
            }
            else
            {
                ErrorMessage = $"Не удалось отправить сообщение: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task LoadOlderMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading) return;

        try
        {
            IsLoadingOlderMessages = true;
            await MessageManager.LoadOlderMessagesAsync(Context.LifetimeToken);
            PollsCount = MessageManager.GetPollsCount();
        }
        finally
        {
            IsLoadingOlderMessages = false;
        }
    }

    [RelayCommand]
    private async Task LoadNewerMessages()
    {
        if (Context.IsDisposed || MessageManager.IsLoading || !MessageManager.HasMoreNewer)
            return;

        await MessageManager.LoadNewerMessagesAsync(Context.LifetimeToken);
        PollsCount = MessageManager.GetPollsCount();
    }

    #endregion

    #region UI и Вложения

    [RelayCommand]
    private void ToggleInfoPanel() => IsInfoPanelOpen = !IsInfoPanelOpen;

    private void OpenInfoSection(InfoSectionType section)
    {
        if (section == InfoSectionType.None)
        {
            IsInfoSectionOpen = false;
            CurrentInfoSection = InfoSectionType.None;
            InfoSectionTitle = string.Empty;
            return;
        }

        CurrentInfoSection = section;
        InfoSectionTitle = InfoSectionTitles.GetValueOrDefault(section, string.Empty);
        IsInfoSectionOpen = true;
    }

    [RelayCommand]
    private async Task OpenPinnedSection()
    {
        if (!IsInfoPanelOpen)
            IsInfoPanelOpen = true;

        OpenInfoSection(InfoSectionType.Pinned);
        await LoadPinnedAsync(Context.LifetimeToken);
    }

    [RelayCommand]
    private async Task ScrollToPinnedMessage()
    {
        if (PinnedBannerMessage == null) return;
        await Search.ScrollToMessageAsync(PinnedBannerMessage.Id);
    }

    [RelayCommand]
    private void OpenPhotosSection() => OpenInfoSection(InfoSectionType.Photos);

    [RelayCommand]
    private void OpenFilesSection() => OpenInfoSection(InfoSectionType.Files);

    [RelayCommand]
    private void OpenPollsSection()
    {
        OpenInfoSection(InfoSectionType.Polls);
        InfoPanel.OnPollsSectionOpened(PollMessages);
    }

    [RelayCommand]
    private void OpenMembersSection()
    {
        OpenInfoSection(InfoSectionType.Members);
        InfoPanel.OnMembersSectionOpened();
    }

    [RelayCommand]
    private void CloseInfoSection() => OpenInfoSection(InfoSectionType.None);

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
        await MarkMessagesAsReadAsync();
    }

    [RelayCommand]
    private void RemoveAttachment(LocalFileAttachment attachment) => Attachments.Remove(attachment);

    [RelayCommand]
    private void InsertEmoji(string emoji) => NewMessage += emoji;

    [RelayCommand]
    private void SelectMention(UserDto? user)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.Username)) return;

        var text = NewMessage ?? string.Empty;
        var caret = Math.Clamp(_composerCaretIndex, 0, text.Length);
        if (!TryFindMentionToken(text, caret, out var start, out _)) return;

        var prefix = text[..start];
        var suffix = caret < text.Length ? text[caret..] : string.Empty;
        NewMessage = $"{prefix}@{user.Username} {suffix}";
        _composerCaretIndex = (prefix + "@" + user.Username + " ").Length;
        IsMentionSuggestionsOpen = false;
        MentionSelectedIndex = -1;
    }

    public bool HandleMentionNavigationKey(Key key)
    {
        if (!IsMentionSuggestionsOpen || MentionSuggestions.Count == 0) return false;

        if (key == Key.Down) { MentionSelectedIndex = NextMentionIndex(MentionSelectedIndex); return true; }
        if (key == Key.Up) { MentionSelectedIndex = PrevMentionIndex(MentionSelectedIndex); return true; }
        if (key == Key.Enter) return TryConfirmMentionSelection();
        if (key == Key.Escape)
        {
            IsMentionSuggestionsOpen = false;
            MentionSelectedIndex = -1;
            return true;
        }

        return false;
    }

    private int NextMentionIndex(int current) => current < MentionSuggestions.Count - 1 ? current + 1 : 0;
    private int PrevMentionIndex(int current) => current > 0 ? current - 1 : MentionSuggestions.Count - 1;

    private bool TryConfirmMentionSelection()
    {
        if (MentionSelectedIndex < 0 || MentionSelectedIndex >= MentionSuggestions.Count)
            return false;

        SelectMention(MentionSuggestions[MentionSelectedIndex]);
        return true;
    }

    public void OnComposerSelectionChanged(int caretIndex)
    {
        _composerCaretIndex = caretIndex;
        UpdateMentionSuggestions(NewMessage, caretIndex);
    }

    [RelayCommand]
    private async Task AttachFile()
    {
        if (!await Attachments.PickAndAddFilesAsync())
            ErrorMessage = "Не удалось выбрать файлы";
    }

    #endregion

    #region Mentions

    private void UpdateMentionSuggestions(string? text, int caretIndex)
    {
        var source = Context.Members.Where(m => m.Id != Context.CurrentUserId && !string.IsNullOrWhiteSpace(m.Username)).ToList();

        if (!TryFindMentionToken(text ?? string.Empty, caretIndex, out _, out var token))
        {
            MentionSuggestions.Clear();
            IsMentionSuggestionsOpen = false;
            MentionSelectedIndex = -1;
            return;
        }

        var filtered = source.Where(m => m.Username!.Contains(token, StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Username!.StartsWith(token, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.Username).Take(7).ToList();

        MentionSuggestions.Clear();
        foreach (var member in filtered)
            MentionSuggestions.Add(member);

        IsMentionSuggestionsOpen = MentionSuggestions.Count > 0;
        MentionSelectedIndex = IsMentionSuggestionsOpen ? 0 : -1;
    }

    private static bool TryFindMentionToken(string text, int caretIndex, out int tokenStart, out string token)
    {
        tokenStart = -1;
        token = string.Empty;

        if (string.IsNullOrEmpty(text) || caretIndex < 0 || caretIndex > text.Length)
            return false;

        var i = caretIndex - 1;
        while (i >= 0 && !char.IsWhiteSpace(text[i])) i--;

        tokenStart = i + 1;
        if (tokenStart >= text.Length || text[tokenStart] != '@') return false;
        if (caretIndex <= tokenStart) return false;

        token = text.Substring(tokenStart + 1, caretIndex - tokenStart - 1);
        return token.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    #endregion

    #region Navigation

    [RelayCommand]
    private async Task OpenCreatePoll()
        => await _navigator.ShowPollDialogAsync(Context.ChatId, () => MessageManager.LoadInitialMessagesAsync());

    [RelayCommand]
    private async Task OpenEditChat()
    {
        if (!InfoPanel.IsGroupChat || Context.Chat == null || !CanEditGroupChat) return;

        await _navigator.ShowEditGroupDialogAsync(Context.Chat, updatedChat =>
        {
            if (_chatMetaUpdatedExternally)
            {
                updatedChat.Avatar = Context.Chat?.Avatar ?? updatedChat.Avatar;
                _chatMetaUpdatedExternally = false;
            }

            Context.Chat = updatedChat;
            _ = RefreshChatPermissionsAsync();
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
        var confirmDialog = new ConfirmDialogViewModel(
            "Покинуть группу",
            "Вы уверены, что хотите покинуть группу?",
            "Покинуть",
            "Отмена");
        await Parent.Parent.ShowDialogAsync(confirmDialog);

        if (!await confirmDialog.Result)
            return;

        await SafeExecuteAsync(async ct =>
        {
            var result = await Context.Api.PostAsync(ApiEndpoints.Chats.Leave(Context.ChatId, UserId), null, ct);

            if (result.Success)
                SuccessMessage = "Вы покинули чат";
            else
                ErrorMessage = $"Не удалось выйти из чата: {result.Error}";
        });
    }

    private async Task RefreshChatPermissionsAsync()
    {
        if (Context.Chat == null)
        {
            CanLeaveChat = false;
            CanEditGroupChat = false;
            OnPropertyChanged(nameof(CanLeaveChat));
            OnPropertyChanged(nameof(CanEditGroupChat));
            return;
        }

        CanLeaveChat = !IsDepartmentScopedChat && (!InfoPanel.IsGroupChat || Context.Chat.CreatedById != Context.CurrentUserId);
        OnPropertyChanged(nameof(CanLeaveChat));

        if (!InfoPanel.IsGroupChat)
        {
            CanEditGroupChat = false;
            OnPropertyChanged(nameof(CanEditGroupChat));
            return;
        }

        var canEdit = _isSystemAdmin || Context.Chat.CreatedById == Context.CurrentUserId;

        if (!canEdit)
        {
            var membersResult = await Context.Api.GetAsync<List<ChatMemberDto>>(ApiEndpoints.Chats.MembersDetailed(Context.ChatId), Context.LifetimeToken);
            if (membersResult is { Success: true, Data: not null })
            {
                var currentMember = membersResult.Data.FirstOrDefault(x => x.UserId == Context.CurrentUserId);
                canEdit = currentMember?.Role is ChatRole.Admin or ChatRole.Owner;
            }
        }

        CanEditGroupChat = canEdit;
        OnPropertyChanged(nameof(CanEditGroupChat));
    }


    private void OnMessagePinStateChanged(MessageDto dto)
    {
        Debug.WriteLine($"[ChatVM] OnMessagePinStateChanged: id={dto.Id} IsPinned={dto.IsPinned}");
        Dispatcher.UIThread.Post(() => HandlePinStateChanged(dto));
    }

    private void HandlePinStateChanged(MessageDto dto)
    {
        Debug.WriteLine($"[ChatVM] HandlePinStateChanged: id={dto.Id} IsPinned={dto.IsPinned} disposed={Context.IsDisposed}");
        if (Context.IsDisposed) return;

        if (!dto.IsPinned)
        {
            Debug.WriteLine($"[ChatVM] RemovePinnedMessage: id={dto.Id}");
            RemovePinnedMessage(dto.Id);
            return;
        }

        Debug.WriteLine($"[ChatVM] AddOrUpdatePinnedMessage: id={dto.Id}");
        AddOrUpdatePinnedMessage(dto);
    }

    private void RemovePinnedMessage(int messageId)
    {
        var toRemove = PinnedMessages.FirstOrDefault(m => m.Id == messageId);
        if (toRemove != null)
        {
            bool wasBanner = PinnedBannerMessage?.Id == messageId;
            PinnedMessages.Remove(toRemove);
            toRemove.Dispose();

            if (wasBanner)
                PinnedBannerMessage = PinnedMessages.Count > 0 ? PinnedMessages[0] : null;

            OnPropertyChanged(nameof(PinnedCount));
            OnPropertyChanged(nameof(HasMultiplePinned));
        }
    }

    private void AddOrUpdatePinnedMessage(MessageDto dto)
    {
        var existing = PinnedMessages.FirstOrDefault(m => m.Id == dto.Id);
        if (existing != null)
        {
            existing.ApplyUpdate(dto);
        }
        else
        {
            var vm = CreatePinnedMessageViewModel(dto);
            PinnedMessages.Insert(0, vm);
            OnPropertyChanged(nameof(PinnedCount));
            OnPropertyChanged(nameof(HasMultiplePinned));
        }

        PinnedBannerMessage = PinnedMessages.Count > 0
            ? PinnedMessages[0]
            : null;
    }

    #endregion

    #region Reading and Scrolling
    public void RequestScrollToBottom() => Context.RequestScrollToBottom();

    public async Task OnMessageVisibleAsync(MessageViewModel message)
    {
        if (Context.IsDisposed) return;
        if (!message.IsUnread || message.SenderId == Context.CurrentUserId) return;

        message.IsUnread = false;
        MessageManager.MarkAsReadLocally(message.Id);
        await Context.Hub.MarkMessageAsReadAsync(Context.ChatId, message.Id);
    }

    public async Task MarkMessagesAsReadAsync()
    {
        if (Context.IsDisposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastMarkAsReadTime).TotalSeconds < AppConstants.MarkAsReadCooldownSeconds)
            return;

        _lastMarkAsReadTime = now;
        await Context.Hub.MarkChatAsReadAsync(Context.ChatId);
    }

    public Task OnMessagesVisibleAsync() => MarkMessagesAsReadAsync();

    public Task ScrollToMessageAsync(int messageId) => Search.ScrollToMessageAsync(messageId);
    public void ScrollToMessageFromSearch(MessageViewModel message) => Context.RequestScrollToMessage(message, true);
    public void ScrollToIndexFromSearch(int index) => Context.RequestScrollToIndex(index, true);
    public void ScrollToMessageSilent(MessageViewModel message) => Context.RequestScrollToMessage(message, false);
    public void ScrollToIndexSilent(int index) => Context.RequestScrollToIndex(index, false);

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
                await RefreshChatPermissionsAsync();
            }


            await InfoPanel.ReloadMembersAfterEditAsync();
            RefreshInfoPanelLists();
        }
        catch (OperationCanceledException) { /* Отменено */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Не удалось обновить инфопанель: {ex.Message}");
        }
    }

    #endregion

    #region Dispose

    private void DisposeCore()
    {
        if (Interlocked.CompareExchange(ref _disposeStartedFlag, 1, 0) != 0) return;
        if (Context.IsDisposed) return;

        DisposeCommonResources();

        try
        {
            _ = MessageManager.DisposeAsync().AsTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] MessageManager dispose error: {ex.Message}");
        }

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

    private int _disposeStartedFlag;

    private void DisposeCommonResources()
    {
        _refreshDebounce?.Cancel();
        _refreshDebounce?.Dispose();
        _refreshDebounce = null;

        UnsubscribePropertyForwarding();
        UnsubscribeCallEvents();

        if (_contextScrollToMessage != null)
        {
            Context.ScrollToMessageRequested -= _contextScrollToMessage;
            Context.ScrollToIndexRequested -= _contextScrollToIndex;
            Context.ScrollToBottomRequested -= _contextScrollToBottom;
            _contextScrollToMessage = null;
            _contextScrollToIndex = null;
            _contextScrollToBottom = null;
        }

        _hubSubscriber.Dispose();
        Context.Hub.SetCurrentChat(null);
        Context.Hub.ChatUpdated -= OnChatUpdated;

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
        Attachments.Dispose();

        PinnedBannerMessage = null;

        foreach (var msg in PinnedMessages) msg.Dispose();
        PinnedMessages.Clear();

        foreach (var msg in PollMessages) msg.Dispose();
        PollMessages.Clear();

        PhotosItems.Clear();
        FilesItems.Clear();
        MembersPreview.Clear();
    }
    private volatile bool _chatMetaUpdatedExternally;
    private async void OnChatUpdated(ChatDto chat)
    {
        if (chat.Id != Context.ChatId || Context.IsDisposed) return;

        var oldAvatar = Context.Chat?.Avatar;

        if (oldAvatar == chat.Avatar)
        {
            _chatMetaUpdatedExternally = true;
            Context.Chat = chat;
            await RefreshChatPermissionsAsync();
            return;
        }

        if (!string.IsNullOrEmpty(oldAvatar))
        {
            try
            {
                App.Current.Services
                    .GetRequiredService<AuthenticatedImageLoader>()
                    .InvalidateByRelativePath(oldAvatar);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatVM] Avatar invalidate error: {ex.Message}");
            }
        }

        _chatMetaUpdatedExternally = true;
        Context.Chat = chat;
        await RefreshChatPermissionsAsync();
    }
    #endregion
}