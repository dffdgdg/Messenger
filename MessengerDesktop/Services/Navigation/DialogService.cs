using MessengerDesktop.ViewModels.Dialog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public interface IDialogService
    {
        IReadOnlyList<DialogBaseViewModel> DialogStack { get; }
        DialogBaseViewModel? CurrentDialog { get; }
        bool HasOpenDialogs { get; }
        bool IsDialogVisible { get; }

        event Action? OnDialogStackChanged;
        event Action<bool>? OnDialogAnimationRequested;

        Task ShowAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel;
        Task CloseAsync();
        Task CloseAllAsync();

        void NotifyAnimationComplete();
    }

    public sealed partial class DialogService : ObservableObject, IDialogService, IDisposable
    {
        private readonly List<DialogBaseViewModel> _dialogStack = [];
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _animationLock = new();
        private readonly Channel<CloseRequest> _closeRequests;
        private readonly CancellationTokenSource _processingCts = new();

        private TaskCompletionSource? _animationTcs;
        private CancellationTokenSource? _animationCts;
        private bool _disposed;

        private record CloseRequest(bool CloseAll, TaskCompletionSource Completion);

        [ObservableProperty]
        private DialogBaseViewModel? _currentDialog;

        [ObservableProperty]
        private bool _isDialogVisible;

        public IReadOnlyList<DialogBaseViewModel> DialogStack => _dialogStack.AsReadOnly();
        public event Action? OnDialogStackChanged;
        public event Action<bool>? OnDialogAnimationRequested;

        public bool HasOpenDialogs => _dialogStack.Count > 0;

        public DialogService()
        {
            _closeRequests = Channel.CreateBounded<CloseRequest>(new BoundedChannelOptions(10)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

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
                        {
                            await CloseAllInternalAsync();
                        }
                        else
                        {
                            await CloseInternalAsync();
                        }

                        request.Completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        request.Completion.TrySetException(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on disposal
            }
        }

        public async Task ShowAsync<TViewModel>(TViewModel dialogViewModel)
            where TViewModel : DialogBaseViewModel
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(dialogViewModel);

            await _operationLock.WaitAsync();

            try
            {
                if (CurrentDialog != null)
                {
                    CurrentDialog.CloseRequested -= OnDialogClosed;
                }

                _dialogStack.Add(dialogViewModel);
                CurrentDialog = dialogViewModel;
                dialogViewModel.CloseRequested += OnDialogClosed;

                IsDialogVisible = true;

                OnDialogStackChanged?.Invoke();

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

            var tcs = new TaskCompletionSource();
            await _closeRequests.Writer.WriteAsync(new CloseRequest(true, tcs));
            await tcs.Task;
        }

        private async Task CloseInternalAsync()
        {
            await _operationLock.WaitAsync();

            try
            {
                if (CurrentDialog == null)
                    return;

                var closingDialog = CurrentDialog;

                await RequestAnimationAsync(isOpening: false);

                closingDialog.CloseRequested -= OnDialogClosed;
                _dialogStack.Remove(closingDialog);

                CurrentDialog = _dialogStack.Count > 0 ? _dialogStack[^1] : null;

                if (CurrentDialog != null)
                {
                    CurrentDialog.CloseRequested += OnDialogClosed;
                    await RequestAnimationAsync(isOpening: true);
                }
                else
                {
                    IsDialogVisible = false;
                }

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
                {
                    dialog.CloseRequested -= OnDialogClosed;
                }

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
            TaskCompletionSource tcs;
            CancellationTokenSource cts;
            CancellationTokenSource? previousCts;

            lock (_animationLock)
            {
                previousCts = _animationCts;
                _animationTcs?.TrySetCanceled();

                tcs = new TaskCompletionSource();
                cts = new CancellationTokenSource();

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
            catch (OperationCanceledException)
            {
                // Expected
            }
            finally
            {
                lock (_animationLock)
                {
                    if (_animationCts == cts)
                    {
                        _animationCts = null;
                    }
                    if (_animationTcs == tcs)
                    {
                        _animationTcs = null;
                    }
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

        private async void OnDialogClosed()
        {
            try
            {
                await CloseAsync();
            }
            catch (ObjectDisposedException)
            {
                // Сервис уже освобождён
            }
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(_disposed, nameof(DialogService));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _processingCts.Cancel();
            _closeRequests.Writer.TryComplete();

            foreach (var dialog in _dialogStack)
            {
                dialog.CloseRequested -= OnDialogClosed;
            }

            _dialogStack.Clear();

            _operationLock.Dispose();
            _animationCts?.Dispose();
            _processingCts.Dispose();
        }
    }
}