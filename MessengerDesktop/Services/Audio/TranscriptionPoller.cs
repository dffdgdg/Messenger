using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Audio;

public sealed class TranscriptionPoller(IApiClientService apiClient) : IDisposable
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly ConcurrentDictionary<int, PollEntry> _active = new();
    private readonly CancellationTokenSource _globalCts = new();
    private bool _disposed;

    public void StartPolling(int messageId, Action<TranscriptionResult> onUpdate)
    {
        if (_disposed) return;

        if (_active.ContainsKey(messageId))
            return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token);
        var entry = new PollEntry(cts, onUpdate);

        if (_active.TryAdd(messageId, entry))
        {
            _ = PollLoopAsync(messageId, entry);
        }
        else
        {
            cts.Dispose();
        }
    }

    public void StopPolling(int messageId)
    {
        if (_active.TryRemove(messageId, out var entry))
        {
            try
            {
                entry.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS уже disposed из PollLoopAsync.finally — OK
            }
        }
    }

    private async Task PollLoopAsync(int messageId, PollEntry entry)
    {
        var delays = new[] { 1000, 2000, 4000, 8000, 16000 };
        var attempt = 0;
        const int maxAttempts = 60;

        try
        {
            while (!entry.Cts.Token.IsCancellationRequested && attempt < maxAttempts)
            {
                var delayMs = delays[Math.Min(attempt, delays.Length - 1)];
                await Task.Delay(delayMs, entry.Cts.Token);

                var result = await _apiClient.GetAsync<TranscriptionResult>(ApiEndpoints.Messages.Transcription(messageId), entry.Cts.Token);

                if (result is { Success: true, Data: not null })
                {
                    try
                    {
                        entry.OnUpdate(result.Data);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TranscriptionPoller] Callback error for msg {messageId}: {ex.Message}");
                    }

                    var status = result.Data.Status;
                    if (status is "done" or "failed")
                    {
                        return;
                    }
                }

                attempt++;
            }

            if (attempt >= maxAttempts)
            {
                Debug.WriteLine($"[TranscriptionPoller] Max attempts reached for msg {messageId}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: StopPolling или Dispose
        }
        catch (ObjectDisposedException)
        {
            // CTS disposed — нормально при Dispose
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[TranscriptionPoller] Error for msg {messageId}: {ex.Message}");
        }
        finally
        {
            _active.TryRemove(messageId, out _);
            entry.Cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _globalCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        _globalCts.Dispose();
    }

    private sealed record PollEntry(CancellationTokenSource Cts, Action<TranscriptionResult> OnUpdate);
}

public class TranscriptionResult
{
    public int MessageId { get; set; }
    public int ChatId { get; set; }
    public string? Status { get; set; }
    public string? Transcription { get; set; }
}