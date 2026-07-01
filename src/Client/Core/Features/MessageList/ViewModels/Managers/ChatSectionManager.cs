using Core.Features.Chat.ViewModels.Context;
using Core.Features.Chat.ViewModels.Handlers.InfoPanel;
using Core.Features.Chat.ViewModels.Messages;
using Core.Features.MessageList.ViewModels.Managers;
using Core.Services.Media.Files;

namespace Core.Features.Chat.ViewModels.Managers;

/// <summary>
/// Управляет секциями информационной панели: фото, файлы, опросы.
/// Загружает данные и предоставляет коллекции для View.
/// </summary>
public sealed partial class ChatSectionManager(ChatContext ctx, ChatInfoPanelHandler infoPanel, ChatMessageManager messageManager,
    IFileDownloadService fileDownloadService, IFileDownloadStateService? downloadStateService, INotificationService notificationService,
    int currentUserId) : ObservableObject, IDisposable
{
    [ObservableProperty] public partial int PhotosCount { get; set; }
    [ObservableProperty] public partial int FilesCount { get; set; }
    [ObservableProperty] public partial int PollsCount { get; set; }

    public bool HasPhotos => PhotosCount > 0;
    public bool HasFiles => FilesCount > 0;
    public bool HasPolls => PollsCount > 0;

    public ObservableCollection<ChatInfoPanelMediaItem> PhotosItems => infoPanel.PhotosItems;
    public ObservableCollection<ChatInfoPanelFileItem> FilesItems { get; } = [];
    public ObservableCollection<MessageViewModel> PollMessages { get; } = [];
    public bool HasContactInfoMediaSection(bool hasPinned) => HasPhotos || HasFiles || hasPinned;
    public bool HasGroupInfoMediaSection(bool hasPinned) => HasPhotos || HasFiles || hasPinned || HasPolls;

    public void ApplyCounts(ChatCountsDto counts)
    {
        PhotosCount = counts.MediaCount;
        FilesCount = counts.FilesCount;
        PollsCount = counts.PollsCount;
        NotifyDependents();
    }

    public async Task RefreshCountsAsync()
    {
        if (ctx.IsDisposed) return;
        try
        {
            var result = await ctx.Api.GetAsync<ChatCountsDto>(
                ApiEndpoints.Messages.Counts(ctx.ChatId), ctx.LifetimeToken);

            if (result is not { Success: true, Data: not null }) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ctx.IsDisposed) return;
                ApplyCounts(result.Data);
            });
        }
        catch (OperationCanceledException) { }
    }

    private void NotifyDependents()
    {
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasPolls));
    }
    public async Task LoadPhotosAsync()
    {
        if (ctx.IsDisposed) return;
        try
        {
            var result = await QueryFilesAsync(hasFiles: true);
            if (result == null) return;

            var photos = result.Messages
                .SelectMany(m => (m.Files ?? [])
                    .Where(IsPhoto)
                    .Select(f => new ChatInfoPanelMediaItem(CreateTemp(m), f)))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            infoPanel.SetPhotos(photos);
        }
        catch (OperationCanceledException) { }
    }

    public async Task LoadFilesAsync()
    {
        if (ctx.IsDisposed) return;
        try
        {
            var result = await QueryFilesAsync(hasFiles: true);
            if (result == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ctx.IsDisposed) return;
                FilesItems.Clear();
                foreach (var item in result.Messages
                    .SelectMany(m => (m.Files ?? [])
                        .Where(f => !IsPhoto(f))
                        .Select(f => new ChatInfoPanelFileItem(CreateTemp(m), f)))
                    .OrderByDescending(x => x.CreatedAt))
                {
                    FilesItems.Add(item);
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    public async Task LoadPollsAsync()
    {
        if (ctx.IsDisposed) return;
        try
        {
            var query = new SearchMessagesQueryDto
            { HasPoll = true, PageSize = 50, OldestFirst = false };

            var result = await ctx.Api.PostAsync<SearchMessagesQueryDto, SearchMessagesResponseDto>(
                ApiEndpoints.Messages.ChatSearch(ctx.ChatId), query, ctx.LifetimeToken);

            if (result is not { Success: true, Data: not null }) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ctx.IsDisposed) return;

                foreach (var old in PollMessages)
                    if (messageManager.Messages.All(m => m.Id != old.Id))
                        old.Dispose();

                PollMessages.Clear();

                foreach (var dto in result.Data.Messages.OrderByDescending(m => m.CreatedAt))
                {
                    var existing = messageManager.Messages.FirstOrDefault(m => m.Id == dto.Id);
                    PollMessages.Add(existing ?? CreateTemp(dto));
                }

                infoPanel.SetPolls(PollMessages);
            });
        }
        catch (OperationCanceledException) { }
    }

    public void ClearAll()
    {
        infoPanel.SetPhotos([]);
        infoPanel.SetPolls([]);
        FilesItems.Clear();

        foreach (var msg in PollMessages)
            if (messageManager.Messages.All(m => m.Id != msg.Id))
                msg.Dispose();
        PollMessages.Clear();
    }

    private async Task<SearchMessagesResponseDto?> QueryFilesAsync(bool hasFiles)
    {
        var query = new SearchMessagesQueryDto
        { HasFiles = hasFiles, PageSize = 50, OldestFirst = false };

        var result = await ctx.Api.PostAsync<SearchMessagesQueryDto, SearchMessagesResponseDto>(
            ApiEndpoints.Messages.ChatSearch(ctx.ChatId), query, ctx.LifetimeToken);

        return result is { Success: true, Data: not null } ? result.Data : null;
    }

    private MessageViewModel CreateTemp(MessageDto dto) =>
        new(dto, fileDownloadService, notificationService, null, ctx.Api, currentUserId: currentUserId, stateService: downloadStateService);

    private static bool IsPhoto(MessageFileDto f) => f.PreViewType == "image" && !string.IsNullOrWhiteSpace(f.Url);

    public void Dispose() => ClearAll();
}