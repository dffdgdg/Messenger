using MessengerDesktop.ViewModels.Dialog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessengerDesktop.Services;

public interface IDialogService
{
    IReadOnlyList<DialogBaseViewModel> DialogStack { get; }
    DialogBaseViewModel? CurrentDialog { get; }
    bool HasOpenDialogs { get; }
    bool IsDialogVisible { get; }

    event Action? OnDialogStackChanged;
    event Action<bool>? OnDialogAnimationRequested;

    Task ShowAsync<TViewModel>(TViewModel dialogViewModel)
        where TViewModel : DialogBaseViewModel;
    Task CloseAsync();
    Task CloseAllAsync();
    void NotifyAnimationComplete();
}

public sealed partial class DialogService : ObservableObject, IDialogService, IDisposable
{
    private readonly List<DialogBaseViewModel> _dialogStack = [];
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Lock _animationLock = new();
    private readonly Channel<CloseRequest> _closeRequests;
    private readonly CancellationTokenSource _processingCts = new();

    private TaskCompletionSource? _animationTcs;
    private CancellationTokenSource? _animationCts;
    private bool _disposed;

    private sealed record CloseRequest(bool CloseAll, TaskCompletionSource Completion);

    [ObservableProperty] public partial DialogBaseViewModel? CurrentDialog { get; set; }

    [ObservableProperty] public partial bool IsDialogVisible { get; set; }

    public IReadOnlyList<DialogBaseViewModel> DialogStack => _dialogStack.AsReadOnly();
    public event Action? OnDialogStackChanged;
    public event Action<bool>? OnDialogAnimationRequested;
    public bool HasOpenDialogs => _dialogStack.Count > 0;

    public DialogService()
    {
        _closeRequests = Channel.CreateBounded<CloseRequest>(new BoundedChannelOptions(10) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _ = ProcessCloseRequestsAsync(_processingCts.Token);
    }

    private async Task ProcessCloseRequestsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var request in _closeRequests.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (request.CloseAll)
                        await CloseAllInternalAsync();
                    else
                        await CloseInternalAsync();

                    request.Completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    request.Completion.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException) { /* Expected on disposal */ }
    }

    public async Task ShowAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dialogViewModel);

        await _operationLock.WaitAsync();

        try
        {
            var hadOpenDialogs = HasOpenDialogs;

            CurrentDialog?.CloseRequested -= OnDialogClosedAsync;

            _dialogStack.Add(dialogViewModel);
            CurrentDialog = dialogViewModel;
            dialogViewModel.CloseRequested += OnDialogClosedAsync;

            IsDialogVisible = true;
            OnDialogStackChanged?.Invoke();

            if (!hadOpenDialogs)
                await RequestAnimationAsync(isOpening: true);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task CloseAsync()
    {
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource();
        await _closeRequests.Writer.WriteAsync(new CloseRequest(false, tcs));
        await tcs.Task;
    }

    public async Task CloseAllAsync()
    {
        ThrowIfDisposed();

        await _closeRequests.Writer.WriteAsync(new CloseRequest(true, new TaskCompletionSource()));
        await new TaskCompletionSource().Task;
    }

    private async Task CloseInternalAsync()
    {
        await _operationLock.WaitAsync();

        try
        {
            if (CurrentDialog == null)
                return;

            var closingDialog = CurrentDialog;
            var hasUnderlyingDialog = _dialogStack.Count > 1;

            if (!hasUnderlyingDialog)
                await RequestAnimationAsync(isOpening: false);

            closingDialog.CloseRequested -= OnDialogClosedAsync;
            _dialogStack.Remove(closingDialog);

            CurrentDialog = _dialogStack.Count > 0
                ? _dialogStack[^1] : null;

            if (CurrentDialog != null)
                CurrentDialog.CloseRequested += OnDialogClosedAsync;
            else
                IsDialogVisible = false;

            OnDialogStackChanged?.Invoke();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task CloseAllInternalAsync()
    {
        await _operationLock.WaitAsync();

        try
        {
            if (!HasOpenDialogs)
                return;

            await RequestAnimationAsync(isOpening: false);

            foreach (var dialog in _dialogStack)
                dialog.CloseRequested -= OnDialogClosedAsync;

            _dialogStack.Clear();
            CurrentDialog = null;
            IsDialogVisible = false;

            OnDialogStackChanged?.Invoke();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task RequestAnimationAsync(bool isOpening)
    {
        var tcs = new TaskCompletionSource();
        var cts = new CancellationTokenSource();

        CancellationTokenSource? previousCts;

        lock (_animationLock)
        {
            previousCts = _animationCts;
            _animationTcs?.TrySetCanceled();

            _animationTcs = tcs;
            _animationCts = cts;
        }

        if (previousCts is not null)
            await previousCts.CancelAsync();

        OnDialogAnimationRequested?.Invoke(isOpening);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(1));

            timeoutCts.Token.Register(() =>
            {
                lock (_animationLock)
                {
                    _animationTcs?.TrySetResult();
                }
            });

            await tcs.Task;
        }
        catch (OperationCanceledException) { /* Expected */ }
        finally
        {
            lock (_animationLock)
            {
                if (_animationCts == cts)
                    _animationCts = null;
                if (_animationTcs == tcs)
                    _animationTcs = null;
            }

            cts.Dispose();
        }
    }

    public void NotifyAnimationComplete()
    {
        lock (_animationLock)
        {
            _animationTcs?.TrySetResult();
        }
    }

    private async Task OnDialogClosedAsync()
    {
        try
        {
            await CloseAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DialogService] OnDialogClosed error: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(DialogService));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _processingCts.Cancel();
        _closeRequests.Writer.TryComplete();

        try
        {
            _operationLock.Wait(TimeSpan.FromSeconds(2));
            try
            {
                foreach (var dialog in _dialogStack)
                    dialog.CloseRequested -= OnDialogClosedAsync;
                _dialogStack.Clear();
            }
            finally
            {
                _operationLock.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DialogService] Dispose lock timeout: {ex.Message}");
            foreach (var dialog in _dialogStack)
                dialog.CloseRequested -= OnDialogClosedAsync;
            _dialogStack.Clear();
        }

        _operationLock.Dispose();
        _animationCts?.Dispose();
        _processingCts.Dispose();
    }
}