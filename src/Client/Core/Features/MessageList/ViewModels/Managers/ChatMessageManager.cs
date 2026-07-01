using Core.Features.Chat.ViewModels.Commands;
using Core.Features.Chat.ViewModels.Context;
using Core.Features.Chat.ViewModels.Messages;
using Core.Features.ChatList.ViewModels.Factories;
using Core.Infrastructure;
using Core.Services.Api.Abstraction;
using Core.Services.Media.Files;
using System.Diagnostics;
using System.Windows.Input;

namespace Core.Features.MessageList.ViewModels.Managers;

/// <summary>
/// Фасад: координирует MessageCollection и MessageLoader.
/// </summary>
public sealed class ChatMessageManager : IAsyncDisposable
{
    private readonly ChatContext _ctx;
    private readonly MessageCollection _collection;
    private readonly MessageLoader _loader;
    private readonly IApiClientService _apiClient;
    private readonly Func<ObservableCollection<UserDto>> _getMembers;
    private readonly ChatCommands? _chatCommands;
    private readonly ICommand? _mentionClickCommand;
    private readonly IFileDownloadService? _downloadService;
    private readonly IFileDownloadStateService? _stateService;
    private readonly INotificationService? _notificationService;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly IDownloadManager? _downloadManager;

    private Dictionary<int, UserDto>? _membersLookup;

    public RangeObservableCollection<MessageViewModel> Messages => _collection.Messages;
    public bool IsLoading => _loader.IsLoading;
    public bool HasMoreOlder => _collection.HasMoreOlder;
    public bool HasMoreNewer => _collection.HasMoreNewer;
    public int? LastReadMessageId => _collection.LastReadMessageId;
    public int? FirstUnreadMessageId => _collection.FirstUnreadMessageId;

    public ChatMessageManager(ChatContext context, MediaServices media, ChatCommands? chatCommands = null, ICommand? mentionClickCommand = null)
    {
        _ctx = context;
        _chatCommands = chatCommands;
        _mentionClickCommand = mentionClickCommand;
        _apiClient = context.Api;
        _getMembers = () => context.Members;
        _downloadService = context.FileDownload;
        _stateService = context.FileDownloadState;
        _notificationService = context.Notifications;
        _audioPlayer = media.AudioPlayer;
        _downloadManager = context.DownloadManager;
        _collection = new MessageCollection(CreateViewModel);
        _loader = new MessageLoader(context.Api, context.Cache, _collection, context.ChatId, context.CurrentUserId);
    }

    public void SetReadInfo(ChatReadInfoDto? info)
    {
        if (info == null) return;
        _collection.SetReadInfo(info.LastReadMessageId, info.FirstUnreadMessageId);
        Debug.WriteLine($"[MessageManager] ReadInfo: lastRead={info.LastReadMessageId}, firstUnread={info.FirstUnreadMessageId}");
    }

    public void InvalidateMembersCache() => _membersLookup = null;

    public Task<int?> LoadInitialMessagesAsync(int? targetMessageId = null, CancellationToken ct = default)
        => _loader.LoadInitialAsync(targetMessageId, ct);

    public Task<int?> LoadMessagesAroundAsync(int messageId, CancellationToken ct = default)
        => _loader.LoadAroundAsync(messageId, ct);

    public Task LoadOlderMessagesAsync(CancellationToken ct = default)
        => _loader.LoadOlderAsync(ct);

    public Task LoadNewerMessagesAsync(CancellationToken ct = default)
        => _loader.LoadNewerAsync(ct);

    public Task GapFillAfterReconnectAsync(CancellationToken ct = default)
        => _loader.GapFillAsync(ct);

    public Task ResetToLatestAsync(CancellationToken ct)
        => _loader.ResetToLatestAsync(ct);

    public void AddReceivedMessage(MessageDto message)
    {
        _membersLookup = null;
        var vm = _collection.AddIncoming(message, _ctx.CurrentUserId);
        if (vm == null) return;

        _ = SafeCacheIncomingAsync(message);
    }

    public void HandleMessageDeleted(int messageId)
    {
        _collection.MarkDeleted(messageId);
        _ = SafeCacheAsync(() => _ctx.Cache?.MarkMessageDeletedAsync(messageId) ?? Task.CompletedTask);
    }

    public void HandleMessageUpdated(MessageDto dto)
    {
        _collection.ApplyUpdate(dto);
        _ = SafeCacheAsync(() => _ctx.Cache?.UpsertMessageAsync(dto) ?? Task.CompletedTask);
    }

    public void HandlePollUpdated(PollDto pollDto)
    {
        if (!HasLoadedPoll(pollDto.Id)) return;

        _ = Task.Run(async () =>
        {
            var current = await FetchPollForCurrentUserAsync(pollDto.Id);
            if (current == null) return;
            await Dispatcher.UIThread.InvokeAsync(() => ApplyPollUpdate(current));
        });
    }

    private void ApplyPollUpdate(PollDto pollDto)
    {
        foreach (var msg in Messages.Where(m => m.PollDto?.Id == pollDto.Id || m.Poll?.PollId == pollDto.Id))
        {
            msg.UpdatePoll(pollDto);
        }
    }

    private bool HasLoadedPoll(int pollId)
        => Messages.Any(m => m.PollDto?.Id == pollId || m.Poll?.PollId == pollId);

    private async Task<PollDto?> FetchPollForCurrentUserAsync(int pollId)
    {
        var result = await _apiClient.GetAsync<PollDto>(ApiEndpoints.Polls.ById(pollId, _ctx.CurrentUserId));
        return result is { Success: true, Data: not null } ? result.Data : null;
    }


    public void MarkAsReadLocally(int messageId)
        => _collection.MarkAsReadUpTo(messageId, _ctx.CurrentUserId);

    public IEnumerable<MessageViewModel> GetUnreadMessages()
        => _collection.GetUnread(_ctx.CurrentUserId);

    private MessageViewModel CreateViewModel(MessageDto msg)
    {
        var lookup = GetMembersLookup();
        lookup.TryGetValue(msg.SenderId ?? 0, out var sender);
        msg.IsOwn = msg.SenderId == _ctx.CurrentUserId;

        return new MessageViewModel(msg, _downloadService, _notificationService, _audioPlayer, _apiClient, _ctx.CurrentUserId, _stateService, _downloadManager)
        {
            SenderName = sender?.DisplayName ?? sender?.Username ?? msg.SenderName ?? "Unknown",
            SenderAvatar = sender?.Avatar ?? msg.SenderAvatarUrl,
            MentionClickCommand = _mentionClickCommand,
            Commands = _chatCommands,
            IsUnread = _collection.LastReadMessageId.HasValue
                       && msg.Id > _collection.LastReadMessageId.Value
                       && msg.SenderId != _ctx.CurrentUserId
        };
    }

    private Dictionary<int, UserDto> GetMembersLookup()
        => _membersLookup ??= _getMembers().ToDictionary(m => m.Id);

    private async Task SafeCacheIncomingAsync(MessageDto msg)
    {
        try
        {
            if (_ctx.Cache != null)
                await _ctx.Cache.UpsertMessageAsync(msg);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MessageManager] Cache incoming error: {ex.Message}");
        }
    }

    private static async Task SafeCacheAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        { Debug.WriteLine($"[MessageManager] Cache error: {ex.Message}"); }
    }

    public async ValueTask DisposeAsync()
    {
        await _loader.DisposeAsync();
        _collection.Dispose();
        _membersLookup = null;
    }
}