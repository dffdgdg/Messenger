using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.ViewModels.Dialog;
using System;
using System.Collections.Generic;
using System.Threading;
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

    public partial class DialogService : ObservableObject, IDialogService
    {
        private readonly List<DialogBaseViewModel> _dialogStack = [];
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _animationLock = new();

        private TaskCompletionSource? _animationTcs;
        private CancellationTokenSource? _animationCts;

        [ObservableProperty]
        private DialogBaseViewModel? _currentDialog;

        [ObservableProperty]
        private bool _isDialogVisible;

        public IReadOnlyList<DialogBaseViewModel> DialogStack => _dialogStack.AsReadOnly();
        public event Action? OnDialogStackChanged;
        public event Action<bool>? OnDialogAnimationRequested;

        public bool HasOpenDialogs => _dialogStack.Count > 0;

        public async Task ShowAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel
        {
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

                OnDialogStackChanged?.Invoke();

                IsDialogVisible = true;

                await Task.Delay(16);

                await RequestAnimationAsync(isOpening: true);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task CloseAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                return;
            }

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

        public async Task CloseAllAsync()
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

            lock (_animationLock)
            {
                _animationCts?.Cancel();
                _animationTcs?.TrySetCanceled();

                tcs = new TaskCompletionSource();
                cts = new CancellationTokenSource();

                _animationTcs = tcs;
                _animationCts = cts;
            }

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
            => await CloseAsync();
    }
}