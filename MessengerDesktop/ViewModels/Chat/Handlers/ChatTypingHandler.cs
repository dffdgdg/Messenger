using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed partial class ChatTypingHandler : ChatFeatureHandler
{
    private readonly Dictionary<int, DateTime> _typingUsers = [];
    private CancellationTokenSource? _cleanupCts;
    private bool _cleanupRunning;

    [ObservableProperty]
    public partial string TypingText { get; set; } = string.Empty;

    public ChatTypingHandler(ChatContext context) : base(context)
        => Ctx.Hub.UserTyping += OnUserTyping;

    public void NotifyTextChanged(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = Ctx.Hub.SendTypingAsync(Ctx.ChatId);
    }

    private void OnUserTyping(int chatId, int userId)
    {
        if (chatId != Ctx.ChatId || userId == Ctx.CurrentUserId) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsAlive) return;

            _typingUsers[userId] = DateTime.UtcNow;
            UpdateTypingText();
            EnsureCleanupRunning();
        });
    }

    private void EnsureCleanupRunning()
    {
        if (_cleanupRunning) return;
        _cleanupRunning = true;

        _cleanupCts?.Dispose();
        _cleanupCts = new CancellationTokenSource();

        _ = RunCleanupLoopAsync(_cleanupCts.Token);
    }

    private async Task RunCleanupLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);

                var now = DateTime.UtcNow;
                var expired = _typingUsers.Where(p => (now - p.Value).TotalMilliseconds > AppConstants.TypingIndicatorDurationMs)
                    .Select(p => p.Key).ToList();

                if (expired.Count > 0)
                    await Dispatcher.UIThread.InvokeAsync(() => RemoveAndUpdate(expired));

                if (_typingUsers.Count == 0)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally { _cleanupRunning = false; }
    }

    private void RemoveAndUpdate(List<int> userIds)
    {
        foreach (var id in userIds)
            _typingUsers.Remove(id);
        UpdateTypingText();
    }

    private void UpdateTypingText()
    {
        if (_typingUsers.Count == 0)
        {
            TypingText = string.Empty;
            return;
        }

        var names = _typingUsers.Keys.Select(ResolveDisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

        TypingText = names.Count switch
        {
            0 => "Кто-то печатает...",
            1 => $"{names[0]} печатает...",
            _ => "Несколько человек печатают..."
        };
    }

    private string ResolveDisplayName(int memberId)
    {
        var member = Ctx.Members.FirstOrDefault(m => m.Id == memberId);
        return member?.DisplayName ?? member?.Username ?? "Пользователь";
    }

    public override void Dispose()
    {
        Ctx.Hub.UserTyping -= OnUserTyping;
        _cleanupCts?.Cancel();
        _cleanupCts?.Dispose();
        _typingUsers.Clear();
        base.Dispose();
    }
}