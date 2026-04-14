using Avalonia.Input;
using Avalonia.Platform.Storage;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Infrastructure;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat.Managers;
using MessengerDesktop.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatViewModel : BaseViewModel, IAsyncDisposable
{
    public enum InfoSectionType { None, Photos, Files, Polls, Members, Pinned }

    private static readonly Dictionary<InfoSectionType, string> InfoSectionTitles = new()
    {
        [InfoSectionType.Photos] = "Медиа",
        [InfoSectionType.Files] = "Документы",
        [InfoSectionType.Polls] = "Опросы",
        [InfoSectionType.Members] = "Участники",
        [InfoSectionType.Pinned] = "Закреплённые сообщения"
    };

    #region Зависимости и хэндлеры

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

    #endregion

    #region Проксированные коллекции и свойства
    public string MemberSearchQuery
    {
        get => InfoPanel.MemberSearchQuery;
        set => InfoPanel.MemberSearchQuery = value;
    }

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
    ["😀", "😂", "😍", "🥰", "😊", "😎", "🤔", "😅", "😭", "😤", "❤", "👍", "👎", "🎉", "🔥", "✨", "💯", "🙏", "👏", "🤝", "💪", "🎁", "📱", "💻", "🎮", "🎵", "📷", "🌟", "⭐", "🌈", "☀️", "🌙"];

    #endregion

    #region Events

    public event Action<MessageViewModel, bool>? ScrollToMessageRequested;
    public event Action<int, bool>? ScrollToIndexRequested;
    public event Action? ScrollToBottomRequested;

    #endregion

    #region Init

    public ChatViewModel(int chatId, ChatsViewModel parent, IChatNavigator navigator, IApiClientService apiClient, IAuthManager authManager, IChatInfoPanelStateStore chatInfoPanelStateStore,
        INotificationService notificationService, IChatNotificationApiService notificationApiService, IDialogService dialogService, IGlobalHubConnection globalHub, IFileDownloadService fileDownloadService,
        IStorageProvider? storageProvider = null, ILocalCacheService? cacheService = null, IAudioPlayerService? audioPlayer = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        var currentUserId = authManager?.Session.UserId ?? 0;
        UserId = currentUserId;

        Context = new ChatContext(chatId, currentUserId, apiClient ?? throw new ArgumentNullException(nameof(apiClient)), dialogService ?? throw new ArgumentNullException(nameof(dialogService)),
            globalHub ?? throw new ArgumentNullException(nameof(globalHub)), notificationService ?? throw new ArgumentNullException(nameof(notificationService)),
            notificationApiService ?? throw new ArgumentNullException(nameof(notificationApiService)), fileDownloadService ?? throw new ArgumentNullException(nameof(fileDownloadService)), cacheService);

        Context.ScrollToMessageRequested += (msg, hl) => ScrollToMessageRequested?.Invoke(msg, hl);
        Context.ScrollToIndexRequested += (idx, hl) => ScrollToIndexRequested?.Invoke(idx, hl);
        Context.ScrollToBottomRequested += () => ScrollToBottomRequested?.Invoke();

        globalHub.SetCurrentChat(chatId);

        MessageManager = new ChatMessageManager(chatId, currentUserId, apiClient, () => Context.Members, fileDownloadService, notificationService, cacheService, audioPlayer, OpenMentionProfileCommand);
        Attachments = new ChatAttachmentManager(chatId, apiClient, storageProvider);
        MemberLoader = new ChatMemberLoader(chatId, currentUserId, apiClient);

        EditDelete = new ChatEditDeleteHandler(Context);
        Reply = new ChatReplyHandler(Context, MessageManager);
        Forward = new ChatForwardHandler(Context);
        Typing = new ChatTypingHandler(Context);
        Voice = new ChatVoiceHandler(Context, () => Reply.CancelReply());
        InfoPanel = new ChatInfoPanelHandler(Context, chatInfoPanelStateStore, MemberLoader);
        Search = new ChatSearchHandler(Context, MessageManager);
        Notification = new ChatNotificationHandler(Context);

        _hubSubscriber = new ChatHubSubscriber(Context, MessageManager, count => UnreadCount = count, OnHubReconnectedAsync);

        _hubSubscriber.Subscribe();
        SubscribePropertyForwarding();

        Context.Chat = new ChatDto
        {
            Id = chatId,
            Name = "Загрузка...",
            Type = ChatType.Chat
        };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsInitialLoading = true;

            var chatResult = await Context.Api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(Context.ChatId), Context.LifetimeToken);

            if (chatResult is { Success: true, Data: not null })
            {
                if (!string.IsNullOrEmpty(chatResult.Data.Avatar))
                    chatResult.Data.Avatar = AvatarHelper.GetUrlWithCacheBuster(chatResult.Data.Avatar);
                Context.Chat = chatResult.Data;
            }
            else
            {
                throw new System.Net.Http.HttpRequestException($"Не удалось загрузить чат: {chatResult.Error}");
            }
            await LoadPinnedAsync(Context.LifetimeToken, updateBanner: true);

            Context.Members = await MemberLoader.LoadMembersAsync(Context.Chat, Context.LifetimeToken);

            if (InfoPanel.IsContactChat)
                await InfoPanel.LoadContactUserAsync();

            OnPropertyChanged(nameof(InfoPanel));

            var readInfo = await Context.Hub.GetReadInfoAsync(Context.ChatId);
            MessageManager.SetReadInfo(readInfo);

            var scrollToIndex = await MessageManager.LoadInitialMessagesAsync(Context.LifetimeToken);

            if (scrollToIndex < Messages.Count - 1)
            {
                Debug.WriteLine($"[ChatVM] Init chat={Context.ChatId} request=ScrollToIndex index={scrollToIndex.Value}");
                Context.RequestScrollToIndex(scrollToIndex.Value);
            }
            else
            {
                Debug.WriteLine($"[ChatVM] Init chat={Context.ChatId} request=ScrollToBottom");
                Context.RequestScrollToBottom();
            }

            await Notification.LoadSettingsAsync(Context.LifetimeToken);

            PollsCount = MessageManager.GetPollsCount();
            RefreshInfoPanelLists();

            var audioRecorder = App.Current.Services.GetRequiredService<IAudioRecorderService>();
            Voice.Initialize(audioRecorder);

            InfoPanel.Subscribe();

            _initTcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _initTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Ошибка инициализации: {ex.Message}");
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

    private void ForwardProperties(INotifyPropertyChanged source, params (string sourceProp, string targetProp)[] mappings)
    {
        var lookup = mappings.GroupBy(m => m.sourceProp).ToDictionary(g => g.Key, g => g.Select(x => x.targetProp).ToArray());

        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null && lookup.TryGetValue(e.PropertyName, out var targets))
            {
                foreach (var target in targets)
                    OnPropertyChanged(target);
            }
        };
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
            (nameof(ChatInfoPanelHandler.MemberSearchQuery), nameof(MemberSearchQuery)));

        ForwardProperties(Context,
            (nameof(ChatContext.Chat), nameof(Chat)),
            (nameof(ChatContext.Members), nameof(Members)),
            (nameof(ChatContext.Members), nameof(InfoPanelSubtitle)));

        MessageManager.Messages.CollectionChanged += (_, _) => RefreshInfoPanelLists();
        MessageManager.MessagePinStateChanged += OnMessagePinStateChanged;

        Context.Members.CollectionChanged += (_, _) => RefreshInfoPanelLists();
        InfoPanel.FilteredMembers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredMembers));

        ForwardProperties(Notification,
            (nameof(ChatNotificationHandler.IsLoadingMuteState), nameof(IsLoadingMuteState)),
            (nameof(ChatNotificationHandler.IsNotificationEnabled), nameof(IsChatNotificationsEnabled)));
    }

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

    private void RefreshInfoPanelLists()
    {
        ReplaceCollection(MembersPreview, [.. Context.Members.Take(5)]);

        var visible = MessageManager.Messages.Where(m => !m.IsDeleted && !m.IsSystemMessage).ToList();

        var photos = visible.SelectMany(m => m.Files.Where(f => f.PreviewType == "image" && !string.IsNullOrWhiteSpace(f.Url)).Select(f => new ChatInfoPanelMediaItem(m, f))).OrderByDescending(x => x.CreatedAt).ToList();

        var files = visible.SelectMany(m => m.Files.Where(f => f.PreviewType != "image").Select(f => new ChatInfoPanelFileItem(m, f))).OrderByDescending(x => x.CreatedAt).ToList();

        var polls = visible.Where(m => m.Poll != null).OrderByDescending(m => m.CreatedAt).ToList();

        ReplaceCollection(PhotosItems, photos);
        ReplaceCollection(FilesItems, files);
        ReplaceCollection(PollMessages, polls);
        OnPropertyChanged(nameof(PhotosCount));
        OnPropertyChanged(nameof(FilesCount));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    #endregion

    #region Messages
    private async Task LoadPinnedAsync(CancellationToken ct, bool updateBanner = false)
    {
        try
        {
            var result = await Context.Api.GetAsync<List<MessageDto>>(ApiEndpoints.Messages.PinnedForChat(Context.ChatId), ct);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Context.IsDisposed) return;

                if (result is { Success: true, Data.Count: > 0 })
                {
                    RebuildPinnedMessages(result.Data);
                    if (updateBanner)
                    {
                        PinnedBannerMessage?.Dispose();
                        PinnedBannerMessage = CreatePinnedMessageViewModel(result.Data[0]);
                        OnPropertyChanged(nameof(IsPinnedBannerVisible));
                    }
                }
                else if (updateBanner)
                {
                    PinnedBannerMessage?.Dispose();
                    PinnedBannerMessage = null;
                    OnPropertyChanged(nameof(IsPinnedBannerVisible));
                    foreach (var old in PinnedMessages) old.Dispose();
                    PinnedMessages.Clear();
                    OnPropertyChanged(nameof(PinnedCount));
                    OnPropertyChanged(nameof(HasMultiplePinned));
                }
            });
        }
        catch (OperationCanceledException) { /* Отмена действия */ }
        catch (Exception ex) { Debug.WriteLine($"[ChatVM] Ошибка загрузки закреплённых: {ex.Message}"); }
    }

    private void RebuildPinnedMessages(List<MessageDto> dtos)
    {
        foreach (var old in PinnedMessages)
            old.Dispose();

        PinnedMessages.Clear();

        foreach (var dto in dtos)
            PinnedMessages.Add(CreatePinnedMessageViewModel(dto));

        OnPropertyChanged(nameof(PinnedCount));
        OnPropertyChanged(nameof(HasMultiplePinned));
    }

    private MessageViewModel CreatePinnedMessageViewModel(MessageDto dto)
        => new(dto, App.Current.Services.GetRequiredService<IFileDownloadService>(), App.Current.Services.GetRequiredService<INotificationService>(),
            App.Current.Services.GetRequiredService<IAudioPlayerService>(), Context.Api);


    [RelayCommand]
    private async Task CopyUsername() => await InfoPanel.CopyUsernameCommand.ExecuteAsync(null);

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
            RefreshInfoPanelLists();
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
        RefreshInfoPanelLists();
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
    private void OpenPollsSection() => OpenInfoSection(InfoSectionType.Polls);

    [RelayCommand]
    private void OpenMembersSection() => OpenInfoSection(InfoSectionType.Members);

    [RelayCommand]
    private void CloseInfoSection() => OpenInfoSection(InfoSectionType.None);

    [RelayCommand]
    private async Task OpenInfoSectionMessageAsync(MessageViewModel? message)
    {
        if (message == null)
            return;

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
        if (user is null || string.IsNullOrWhiteSpace(user.Username))
            return;

        var text = NewMessage ?? string.Empty;
        var caret = Math.Clamp(_composerCaretIndex, 0, text.Length);
        if (!TryFindMentionToken(text, caret, out var start, out var _))
            return;

        var prefix = text[..start];
        var suffix = caret < text.Length ? text[caret..] : string.Empty;
        NewMessage = $"{prefix}@{user.Username} {suffix}";
        _composerCaretIndex = (prefix + "@" + user.Username + " ").Length;
        IsMentionSuggestionsOpen = false;
        MentionSelectedIndex = -1;
    }

    public bool HandleMentionNavigationKey(Key key)
    {
        if (!IsMentionSuggestionsOpen || MentionSuggestions.Count == 0)
            return false;

        if (key == Key.Down)
        {
            MentionSelectedIndex = MentionSelectedIndex < MentionSuggestions.Count - 1 ? MentionSelectedIndex + 1 : 0;
            return true;
        }

        if (key == Key.Up)
        {
            MentionSelectedIndex = MentionSelectedIndex > 0 ? MentionSelectedIndex - 1 : MentionSuggestions.Count - 1;
            return true;
        }

        if (key == Key.Enter && MentionSelectedIndex >= 0 && MentionSelectedIndex < MentionSuggestions.Count)
        {
            SelectMention(MentionSuggestions[MentionSelectedIndex]);
            return true;
        }

        if (key == Key.Escape)
        {
            IsMentionSuggestionsOpen = false;
            MentionSelectedIndex = -1;
            return true;
        }

        return false;
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

        var filtered = source
            .Where(m => m.Username!.Contains(token, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Username!.StartsWith(token, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.Username)
            .Take(7)
            .ToList();

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
        while (i >= 0 && !char.IsWhiteSpace(text[i]))
            i--;

        tokenStart = i + 1;
        if (tokenStart >= text.Length || text[tokenStart] != '@')
            return false;

        if (caretIndex <= tokenStart)
            return false;

        token = text.Substring(tokenStart + 1, caretIndex - tokenStart - 1);
        return token.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    #endregion

    #region Навигация

    [RelayCommand]
    private async Task OpenCreatePoll() => await _navigator.ShowPollDialogAsync(Context.ChatId, () => MessageManager.LoadInitialMessagesAsync());

    [RelayCommand]
    private async Task OpenEditChat()
    {
        if (!InfoPanel.IsGroupChat || Context.Chat == null) return;

        await _navigator.ShowEditGroupDialogAsync(Context.Chat, updatedChat =>
        {
            Context.Chat = updatedChat;
            Parent.UpdateChatInList(updatedChat);
            _ = InfoPanel.ReloadMembersAfterEditAsync();
        });
    }

    [RelayCommand]
    public async Task OpenProfile(int userId) => await _navigator.ShowUserProfileAsync(userId);

    [RelayCommand]
    private async Task OpenMentionProfile(string? mention)
    {
        if (string.IsNullOrWhiteSpace(mention))
            return;

        var normalizedUsername = mention.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return;

        var user = Members.FirstOrDefault(m =>
            string.Equals(m.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

        if (user?.Id > 0)
            await _navigator.ShowUserProfileAsync(user.Id);
    }

    [RelayCommand]
    private async Task LeaveChat()
    {
        await SafeExecuteAsync(async ct =>
        {
            var result = await Context.Api.PostAsync(ApiEndpoints.Chats.Leave(Context.ChatId, UserId), null, ct);

            if (result.Success)
                SuccessMessage = "Вы покинули чат";
            else
                ErrorMessage = $"Не удалось выйти из чата: {result.Error}";
        });
    }
    private void OnMessagePinStateChanged(MessageDto dto)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Context.IsDisposed) return;

            if (!dto.IsPinned)
            {
                var toRemove = PinnedMessages.FirstOrDefault(m => m.Id == dto.Id);
                if (toRemove != null)
                {
                    PinnedMessages.Remove(toRemove);
                    toRemove.Dispose();
                    OnPropertyChanged(nameof(PinnedCount));
                    OnPropertyChanged(nameof(HasMultiplePinned));
                }

                if (PinnedBannerMessage?.Id == dto.Id)
                {
                    PinnedBannerMessage?.Dispose();
                    PinnedBannerMessage = PinnedMessages.Count > 0
                        ? CreatePinnedMessageViewModel(PinnedMessages[0].Message)
                        : null;
                    OnPropertyChanged(nameof(IsPinnedBannerVisible));
                }
                return;
            }

            var existing = PinnedMessages.FirstOrDefault(m => m.Id == dto.Id);
            if (existing != null)
            {
                existing.ApplyUpdate(dto);
            }
            else
            {
                PinnedMessages.Insert(0, CreatePinnedMessageViewModel(dto));
                OnPropertyChanged(nameof(PinnedCount));
                OnPropertyChanged(nameof(HasMultiplePinned));
            }

            PinnedBannerMessage?.Dispose();
            PinnedBannerMessage = CreatePinnedMessageViewModel(dto);
            OnPropertyChanged(nameof(IsPinnedBannerVisible));
        });
    }

    #endregion

    #region Чтение и скроллинг

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

    #region Переподключение

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
            var chatResult = await Context.Api.GetAsync<ChatDto>(ApiEndpoints.Chats.ById(Context.ChatId), ct);

            if (chatResult is { Success: true, Data: not null })
                Context.Chat = chatResult.Data;

            await InfoPanel.ReloadMembersAfterEditAsync();
            RefreshInfoPanelLists();
        }
        catch (OperationCanceledException) { /* Operation was canceled */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatVM] Не удалось обновить инфопанель: {ex.Message}");
        }
    }

    #endregion

    #region Очистка ресурсов (Dispose)

    private void DisposeCore()
    {
        if (Context.IsDisposed) return;

        DisposeCommonResources();

        try
        {
            MessageManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            DisposeCore();

        base.Dispose(disposing);
    }

    public async ValueTask DisposeAsync()
    {
        if (Context.IsDisposed) return;

        DisposeCommonResources();

        await MessageManager.DisposeAsync();

        Context.Dispose();

        base.Dispose(true);
    }

    private void DisposeCommonResources()
    {
        _hubSubscriber.Dispose();
        Context.Hub.SetCurrentChat(null);

        EditDelete.Dispose();
        Reply.Dispose();
        Forward.Dispose();
        Typing.Dispose();
        Voice.Dispose();
        InfoPanel.Dispose();
        Search.Dispose();
        Notification.Dispose();
        Attachments.Dispose();
        MessageManager.MessagePinStateChanged -= OnMessagePinStateChanged;
        foreach (var msg in PinnedMessages)
            msg.Dispose();
        PinnedMessages.Clear();
    }
    #endregion
}