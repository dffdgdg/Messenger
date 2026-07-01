using Core.Features.Chat.ViewModels.Messages;
using Core.Infrastructure;

namespace Core.Features.MessageList.ViewModels.Managers;

/// <summary>
/// Владеет коллекцией MessageViewModel: индекс, date separators, группировка, trim.
/// </summary>
public sealed class MessageCollection : IDisposable
{
    private const int MaxMessagesInMemory = 300;
    private const int TrimBatchSize = 100;

    private readonly HashSet<int> _loadedIds = [];
    private readonly Dictionary<int, MessageViewModel> _index = [];
    private readonly Func<MessageDto, MessageViewModel> _factory;

    private bool _hasMoreOlder = true;
    private bool _hasMoreNewer;

    public RangeObservableCollection<MessageViewModel> Messages { get; } = [];

    public bool HasMoreOlder
    {
        get => Volatile.Read(ref _hasMoreOlder);
        set => Volatile.Write(ref _hasMoreOlder, value);
    }

    public bool HasMoreNewer
    {
        get => Volatile.Read(ref _hasMoreNewer);
        set => Volatile.Write(ref _hasMoreNewer, value);
    }

    public int? OldestId { get; private set; }
    public int? NewestId { get; private set; }

    public int? LastReadMessageId { get; private set; }
    public int? FirstUnreadMessageId { get; private set; }

    public MessageCollection(Func<MessageDto, MessageViewModel> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void SetReadInfo(int? lastReadMessageId, int? firstUnreadMessageId)
    {
        LastReadMessageId = lastReadMessageId;
        FirstUnreadMessageId = firstUnreadMessageId;
    }

    public void Render(List<MessageDto> dtos)
    {
        DisposeAllMessages();
        Messages.Clear();
        _loadedIds.Clear();
        _index.Clear();
        OldestId = NewestId = null;
        HasMoreOlder = true;
        HasMoreNewer = false;

        var vms = new List<MessageViewModel>(dtos.Count);
        foreach (var dto in dtos.OrderBy(m => m.Id))
        {
            if (!_loadedIds.Add(dto.Id)) continue;
            var vm = _factory(dto);
            vms.Add(vm);
            _index[dto.Id] = vm;
            TrackBounds(dto.Id);
        }

        Messages.AddRange(vms);
        UpdateDateSeparators();
        RecalculateGrouping();
    }

    public void Append(List<MessageDto> dtos)
    {
        var newVms = BuildNewVms(dtos);
        if (newVms.Count == 0) return;

        var startIndex = Messages.Count;
        Messages.AddRange(newVms);
        UpdateDateSeparatorsForRange(startIndex > 0 ? startIndex - 1 : 0, Messages.Count - 1);
        UpdateGroupingFrom(Math.Max(0, startIndex - 1));
        TrimFromStart();
    }

    public void Prepend(List<MessageDto> dtos)
    {
        var newVms = BuildNewVms(dtos);
        if (newVms.Count == 0) return;

        PrecomputeGrouping(newVms);
        Messages.InsertRange(0, newVms);
        FixBoundaryAfterPrepend(newVms.Count);
        UpdateDateSeparatorsForRange(0, Math.Min(newVms.Count, Messages.Count - 1));
        TrimFromEnd();
    }

    public MessageViewModel? AddIncoming(MessageDto dto, int currentUserId)
    {
        if (!_loadedIds.Add(dto.Id)) return null;

        var vm = _factory(dto);
        vm.IsNewIncoming = true;
        if (dto.SenderId != currentUserId) vm.IsUnread = true;

        _index[dto.Id] = vm;
        Messages.Add(vm);
        TrackBounds(dto.Id);
        UpdateDateSeparatorForLast();
        MessageViewModel.UpdateGroupingAround(Messages, Messages.Count - 1);
        TrimFromStart();
        HasMoreNewer = false;

        return vm;
    }

    public void MarkDeleted(int messageId)
    {
        var msg = Find(messageId);
        if (msg != null)
        {
            msg.MarkAsDeleted();
            MessageViewModel.UpdateGroupingAround(Messages, Messages.IndexOf(msg));
        }

        foreach (var reply in Messages.Where(m => m.ReplyToMessageId == messageId))
        {
            reply.ReplyToIsDeleted = true;
            reply.ReplyToContent = null;
        }
    }

    public void ApplyUpdate(MessageDto dto) => Find(dto.Id)?.ApplyUpdate(dto);

    public void MarkAsReadUpTo(int messageId, int currentUserId)
    {
        foreach (var msg in Messages.Where(m => m.Id <= messageId && m.IsUnread))
            msg.IsUnread = false;

        if (!LastReadMessageId.HasValue || messageId > LastReadMessageId.Value)
            LastReadMessageId = messageId;
    }

    public MessageViewModel? Find(int id)
        => _index.TryGetValue(id, out var vm) ? vm : null;

    public int? FindIndex(int id)
    {
        for (var i = 0; i < Messages.Count; i++)
            if (Messages[i].Id == id) return i;
        return null;
    }

    public bool Contains(int id) => _loadedIds.Contains(id);

    public bool TryAdd(int id) => _loadedIds.Add(id);

    public IEnumerable<MessageViewModel> GetUnread(int currentUserId)
        => Messages.Where(m => m.IsUnread && m.SenderId != currentUserId);

    public int? LastIndex => Messages.Count > 0 ? Messages.Count - 1 : null;

    public void SetBounds(bool hasOlder, bool hasNewer)
    {
        HasMoreOlder = hasOlder;
        HasMoreNewer = hasNewer;
    }

    public void RecalculateBounds()
    {
        if (Messages.Count == 0) { OldestId = NewestId = null; return; }
        OldestId = Messages[0].Id;
        NewestId = Messages[^1].Id;
    }

    private void TrackBounds(int id)
    {
        OldestId = OldestId.HasValue ? Math.Min(OldestId.Value, id) : id;
        NewestId = NewestId.HasValue ? Math.Max(NewestId.Value, id) : id;
    }

    private void TrimFromStart()
    {
        if (Messages.Count <= MaxMessagesInMemory) return;
        var count = Math.Min(TrimBatchSize, Messages.Count - MaxMessagesInMemory);
        RemoveRange(Messages.Take(count).ToList());
        HasMoreOlder = true;
        RecalculateBounds();
    }

    private void TrimFromEnd()
    {
        if (Messages.Count <= MaxMessagesInMemory) return;
        var count = Math.Min(TrimBatchSize, Messages.Count - MaxMessagesInMemory);
        RemoveRange(Messages.Skip(Messages.Count - count).ToList());
        HasMoreNewer = true;
        RecalculateBounds();
    }

    private void RemoveRange(List<MessageViewModel> toRemove)
    {
        foreach (var msg in toRemove)
        {
            _loadedIds.Remove(msg.Id);
            _index.Remove(msg.Id);
            DisposeMessage(msg);
        }
        Messages.RemoveRange(toRemove);
    }

    private void UpdateDateSeparators()
    {
        DateTime? prev = null;
        foreach (var msg in Messages)
        {
            var date = msg.CreatedAt.Date;
            var isNew = prev == null || date != prev.Value;
            msg.ShowDateSeparator = isNew;
            msg.DateSeparatorText = isNew ? FormatDate(date) : null;
            prev = date;
        }
    }

    private void UpdateDateSeparatorsForRange(int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            var date = Messages[i].CreatedAt.Date;
            var prevDate = i > 0 ? Messages[i - 1].CreatedAt.Date : (DateTime?)null;
            var isNew = prevDate == null || date != prevDate.Value;
            Messages[i].ShowDateSeparator = isNew;
            Messages[i].DateSeparatorText = isNew ? FormatDate(date) : null;
        }
    }

    private void UpdateDateSeparatorForLast()
    {
        var idx = Messages.Count - 1;
        if (idx < 0) return;
        var vm = Messages[idx];
        var date = vm.CreatedAt.Date;
        var isNew = idx == 0 || date != Messages[idx - 1].CreatedAt.Date;
        vm.ShowDateSeparator = isNew;
        vm.DateSeparatorText = isNew ? FormatDate(date) : null;
    }

    private static string FormatDate(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return "Сегодня";
        if (date == today.AddDays(-1)) return "Вчера";
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        return date.ToString(date.Year == today.Year ? "d MMMM" : "d MMMM yyyy", culture);
    }

    private void RecalculateGrouping()
        => MessageViewModel.RecalculateGrouping(Messages);

    private void UpdateGroupingFrom(int startIndex)
    {
        for (var i = startIndex; i < Messages.Count; i++)
        {
            var prev = i > 0 ? Messages[i - 1] : null;
            var next = i < Messages.Count - 1 ? Messages[i + 1] : null;
            Messages[i].IsContinuation = prev != null && MessageViewModel.CanGroup(prev, Messages[i]);
            Messages[i].HasNextFromSame = next != null && MessageViewModel.CanGroup(Messages[i], next);
        }
    }

    private static void PrecomputeGrouping(List<MessageViewModel> vms)
    {
        for (var i = 0; i < vms.Count; i++)
        {
            var prev = i > 0 ? vms[i - 1] : null;
            var next = i < vms.Count - 1 ? vms[i + 1] : null;
            vms[i].IsContinuation = prev != null && MessageViewModel.CanGroup(prev, vms[i]);
            vms[i].HasNextFromSame = next != null && MessageViewModel.CanGroup(vms[i], next);
        }
    }

    private void FixBoundaryAfterPrepend(int insertedCount)
    {
        if (insertedCount >= Messages.Count) return;
        var lastNew = Messages[insertedCount - 1];
        var firstOld = Messages[insertedCount];
        var link = MessageViewModel.CanGroup(lastNew, firstOld);
        lastNew.HasNextFromSame = link;
        firstOld.IsContinuation = link;
    }

    private List<MessageViewModel> BuildNewVms(List<MessageDto> dtos)
    {
        var result = new List<MessageViewModel>(dtos.Count);
        foreach (var dto in dtos.OrderBy(m => m.Id))
        {
            if (!_loadedIds.Add(dto.Id)) continue;
            var vm = _factory(dto);
            result.Add(vm);
            _index[dto.Id] = vm;
            TrackBounds(dto.Id);
        }
        return result;
    }

    private void DisposeAllMessages()
    {
        foreach (var msg in Messages)
            DisposeMessage(msg);
    }

    private static void DisposeMessage(MessageViewModel msg)
    {
        foreach (var file in msg.FileViewModels)
            (file as IDisposable)?.Dispose();
        msg.Dispose();
    }

    public void Dispose()
    {
        DisposeAllMessages();
        Messages.Clear();
        _index.Clear();
        _loadedIds.Clear();
    }
}