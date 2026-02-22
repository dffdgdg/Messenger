using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public sealed class TranscriptionPoller(IApiClientService apiClient) : IDisposable
{
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
    }

    public void StopPolling(int messageId)
    {
        if (_active.TryRemove(messageId, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
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

                var result = await apiClient.GetAsync<TranscriptionResult>(
                    ApiEndpoints.Message.Transcription(messageId),
                    entry.Cts.Token);

                if (result is { Success: true, Data: not null })
                {
                    entry.OnUpdate(result.Data);

                    var status = result.Data.Status;
                    if (status is "done" or "failed")
                    {
                        StopPolling(messageId);
                        return;
                    }
                }

                attempt++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TranscriptionPoller] Error for msg {messageId}: {ex.Message}");
        }
        finally
        {
            _active.TryRemove(messageId, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _globalCts.Cancel();

        foreach (var kvp in _active)
        {
            kvp.Value.Cts.Dispose();
        }
        _active.Clear();

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