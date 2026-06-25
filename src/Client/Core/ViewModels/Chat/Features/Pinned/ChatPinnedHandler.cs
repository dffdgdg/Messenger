using Core.Services.Features.Media.Files;
using Core.ViewModels.Chat.Context;
using Core.ViewModels.Chat.Shared;
using Shared.Contracts.Message;
using System.Diagnostics;

namespace Core.ViewModels.Chat.Features.Pinned;

public sealed partial class ChatPinnedHandler : ChatFeatureHandler
{
    private readonly IFileDownloadService _downloadService;
    private readonly IFileDownloadStateService? _stateService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly INotificationService _notifications;

    public ObservableCollection<MessageViewModel> PinnedMessages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinnedBannerVisible))]
    [NotifyPropertyChangedFor(nameof(PinnedBannerPreviewText))]
    public partial MessageViewModel? PinnedBannerMessage { get; set; }

    public bool IsPinnedBannerVisible => PinnedBannerMessage != null;
    public bool HasPinnedMessages => PinnedMessages.Count > 0;
    public bool HasMultiplePinned => PinnedMessages.Count > 1;
    public int PinnedCount => PinnedMessages.Count;

    public string PinnedBannerPreviewText
    {
        get
        {
            var msg = PinnedBannerMessage;
            if (msg == null) return string.Empty;
            return string.IsNullOrWhiteSpace(msg.SenderName) ? msg.ContentPreview : $"{msg.SenderName}: {msg.ContentPreview}";
        }
    }

    public ChatPinnedHandler(
        ChatContext context,
        IFileDownloadService downloadService,
        IFileDownloadStateService? stateService,
        IAudioPlayerService audioPlayer,
        INotificationService notifications)
        : base(context)
    {
        _downloadService = downloadService;
        _stateService = stateService;
        _audioPlayer = audioPlayer;
        _notifications = notifications;

        Ctx.MessagePinStateChanged += OnMessagePinStateChanged;
        PinnedMessages.CollectionChanged += OnPinnedCollectionChanged;
    }

    // ── Публичный API ────────────────────────────────────────────────────────

    /// <summary>Первоначальная загрузка при открытии чата.</summary>
    public Task LoadInitialAsync(CancellationToken ct)
        => LoadCoreAsync(ct);

    /// <summary>Перезагрузка при открытии секции закреплённых.</summary>
    public Task ReloadAsync(CancellationToken ct)
        => LoadCoreAsync(ct);

    // ── Загрузка ─────────────────────────────────────────────────────────────

    private async Task LoadCoreAsync(CancellationToken ct)
    {
        try
        {
            var result = await Ctx.Api.GetAsync<List<MessageDto>>(
                ApiEndpoints.Messages.PinnedForChat(Ctx.ChatId), ct);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Ctx.IsDisposed) return;

                var data = result is { Success: true, Data.Count: > 0 }
                    ? result.Data
                    : [];

                RebuildPinnedMessages(data);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PinnedHandler] Ошибка загрузки: {ex.Message}");
        }
    }

    // ── Перестройка коллекции ────────────────────────────────────────────────

    private void RebuildPinnedMessages(List<MessageDto> dtos)
    {
        // Снимаем баннер чтобы не было PropertyChanged на удалённый объект
        var oldBanner = PinnedBannerMessage;
        PinnedBannerMessage = null;
        oldBanner?.PropertyChanged -= OnBannerPropertyChanged;

        foreach (var old in PinnedMessages)
            old.Dispose();

        PinnedMessages.Clear();

        foreach (var dto in dtos)
            PinnedMessages.Add(CreateViewModel(dto));

        SetBanner(PinnedMessages.Count > 0 ? PinnedMessages[0] : null);
        NotifyCounters();
    }

    // ── Hub-событие закрепления / открепления ────────────────────────────────

    private void OnMessagePinStateChanged(MessageDto dto)
        => Dispatcher.UIThread.Post(() => HandlePinStateChanged(dto));

    private void HandlePinStateChanged(MessageDto dto)
    {
        if (Ctx.IsDisposed) return;

        if (!dto.IsPinned)
            RemovePinnedMessage(dto.Id);
        else
            AddOrUpdatePinnedMessage(dto);
    }

    private void RemovePinnedMessage(int messageId)
    {
        var toRemove = PinnedMessages.FirstOrDefault(m => m.Id == messageId);
        if (toRemove == null) return;

        bool wasBanner = PinnedBannerMessage?.Id == messageId;
        PinnedMessages.Remove(toRemove);
        toRemove.Dispose();

        if (wasBanner)
            SetBanner(PinnedMessages.Count > 0 ? PinnedMessages[0] : null);

        NotifyCounters();
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
            PinnedMessages.Insert(0, CreateViewModel(dto));
            NotifyCounters();
        }

        SetBanner(PinnedMessages.Count > 0 ? PinnedMessages[0] : null);
    }

    // ── Баннер ───────────────────────────────────────────────────────────────

    private void SetBanner(MessageViewModel? vm)
    {
        if (PinnedBannerMessage != null)
            PinnedBannerMessage.PropertyChanged -= OnBannerPropertyChanged;

        PinnedBannerMessage = vm;

        if (vm != null)
            vm.PropertyChanged += OnBannerPropertyChanged;
    }

    private void OnBannerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MessageViewModel.ContentPreview)
                           or nameof(MessageViewModel.SenderName)
                           or nameof(MessageViewModel.Content))
        {
            OnPropertyChanged(nameof(PinnedBannerPreviewText));
        }
    }

    // ── Счётчики ─────────────────────────────────────────────────────────────

    private void OnPinnedCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => NotifyCounters();

    private void NotifyCounters()
    {
        OnPropertyChanged(nameof(PinnedCount));
        OnPropertyChanged(nameof(HasPinnedMessages));
        OnPropertyChanged(nameof(HasMultiplePinned));
        OnPropertyChanged(nameof(IsPinnedBannerVisible));
    }

    // ── Фабрика MessageViewModel ─────────────────────────────────────────────

    private MessageViewModel CreateViewModel(MessageDto dto)
        => new(dto, _downloadService, _notifications, _audioPlayer, Ctx.Api,
               currentUserId: Ctx.CurrentUserId, stateService: _stateService);

    // ── Dispose ──────────────────────────────────────────────────────────────

    protected override void DisposeManagedResources()
    {
        Ctx.MessagePinStateChanged -= OnMessagePinStateChanged;
        PinnedMessages.CollectionChanged -= OnPinnedCollectionChanged;

        if (PinnedBannerMessage != null)
        {
            PinnedBannerMessage.PropertyChanged -= OnBannerPropertyChanged;
            PinnedBannerMessage = null;
        }

        foreach (var msg in PinnedMessages)
            msg.Dispose();

        PinnedMessages.Clear();
    }
}