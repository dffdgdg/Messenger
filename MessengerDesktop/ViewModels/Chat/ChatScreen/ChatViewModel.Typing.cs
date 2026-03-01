using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
	private readonly Dictionary<int, DateTime> _typingUsers = [];
    private CancellationTokenSource? _typingIndicatorCts;

    public string TypingText
    {
        get
        {
            if (_typingUsers.Count == 0)
                return string.Empty;

            var names = _typingUsers.Keys
                .Select(ResolveUserDisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            if (names.Count == 0)
                return "Кто-то печатает...";

            if (names.Count == 1)
                return $"{names[0]} печатает...";

            return "Несколько человек печатают...";
        }
    }

    partial void OnNewMessageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _ = _hubConnection?.SendTypingAsync();
    }

    private void OnUserTyping(int cId, int typingUserId)
    {
        if (typingUserId == UserId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _typingUsers[typingUserId] = DateTime.UtcNow;
            OnPropertyChanged(nameof(TypingText));
            StartTypingCleanupLoop();
        });
    }

    private void StartTypingCleanupLoop()
    {
        _typingIndicatorCts?.Cancel();
        _typingIndicatorCts?.Dispose();
        _typingIndicatorCts = new CancellationTokenSource();

        var ct = _typingIndicatorCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(500, ct);
                    var now = DateTime.UtcNow;
                    var thresholdMs = AppConstants.TypingIndicatorDurationMs;
                    var expired = _typingUsers
                        .Where(p => (now - p.Value).TotalMilliseconds > thresholdMs)
                        .Select(p => p.Key)
                        .ToList();

                    if (expired.Count == 0)
                        continue;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var userId in expired)
                            _typingUsers.Remove(userId);

                        OnPropertyChanged(nameof(TypingText));
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }, ct);
    }

    private string ResolveUserDisplayName(int memberId)
    {
        var member = Members.FirstOrDefault(m => m.Id == memberId);
        return member?.DisplayName ?? member?.Username ?? "Пользователь";
    }
}
