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
    private bool _typingCleanupRunning;

    public string TypingText
    {
        get
        {
            if (_typingUsers.Count == 0)
                return string.Empty;

            var names = _typingUsers.Keys
                .Select(ResolveUserDisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct();

            var firstName = names.FirstOrDefault();

            if (firstName is null)
                return "Кто-то печатает...";

            if (!names.Skip(1).Any())
                return $"{firstName} печатает...";

            return "Несколько человек печатают...";
        }
    }

    partial void OnNewMessageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _ = _globalHub.SendTypingAsync(_chatId);
    }

    private void OnUserTyping(int typingUserId)
    {
        if (typingUserId == UserId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            _typingUsers[typingUserId] = DateTime.UtcNow;
            OnPropertyChanged(nameof(TypingText));
            EnsureTypingCleanupLoopRunning();
        });
    }

    /// <summary>
    /// Запускает cleanup loop, если он ещё не запущен.
    /// Не пересоздаёт CTS/Task при каждом событии typing.
    /// Loop завершается сам, когда нет печатающих пользователей.
    /// </summary>
    private void EnsureTypingCleanupLoopRunning()
    {
        if (_typingCleanupRunning)
            return;

        _typingCleanupRunning = true;

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
                    const int thresholdMs = AppConstants.TypingIndicatorDurationMs;

                    var expired = _typingUsers
                        .Where(p => (now - p.Value).TotalMilliseconds > thresholdMs)
                        .Select(p => p.Key).ToList();

                    if (expired.Count > 0)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var userId in expired)
                                _typingUsers.Remove(userId);

                            OnPropertyChanged(nameof(TypingText));
                        });
                    }

                    if (_typingUsers.Count == 0)
                    {
                        _typingCleanupRunning = false;
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on dispose
            }
            finally
            {
                _typingCleanupRunning = false;
            }
        }, ct);
    }

    private string ResolveUserDisplayName(int memberId)
    {
        var member = Members.FirstOrDefault(m => m.Id == memberId);
        return member?.DisplayName ?? member?.Username ?? "Пользователь";
    }
}