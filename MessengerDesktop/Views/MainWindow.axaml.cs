using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly IDialogService _dialogService;
        private readonly IPlatformService _platformService;
        private readonly INotificationService _notificationService;
        private const int AnimationDurationMs = 250;

        private CancellationTokenSource? _animationCts;
        private readonly object _animationLock = new();

        public MainWindow()
        {
            InitializeComponent();

            _platformService = App.Current.Services.GetRequiredService<IPlatformService>();
            _dialogService = App.Current.Services.GetRequiredService<IDialogService>();
            _notificationService = App.Current.Services.GetRequiredService<INotificationService>(); 

            _platformService.Initialize(this);
            _notificationService.Initialize(this); 

            _dialogService.OnDialogAnimationRequested += OnDialogAnimationRequested;
        }

        private void OnDialogAnimationRequested(bool isOpening)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await RunAnimationAsync(isOpening);
                _dialogService.NotifyAnimationComplete();
            });
        }

        private async Task RunAnimationAsync(bool isOpening)
        {
            CancellationTokenSource? oldCts;
            var newCts = new CancellationTokenSource();

            lock (_animationLock)
            {
                oldCts = _animationCts;
                _animationCts = newCts;
            }

            oldCts?.Cancel();
            oldCts?.Dispose();

            try
            {
                ResetAnimationState();

                if (isOpening)
                {
                    await PlayOpenAnimationAsync(newCts.Token);
                }
                else
                {
                    await PlayCloseAnimationAsync(newCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                ResetAnimationState();
            }
        }

        private void ResetAnimationState()
        {
            DialogOverlay.Classes.Remove("Open");
            DialogOverlay.Classes.Remove("Closing");
            DialogAnimWrapper.Classes.Remove("Open");
            DialogAnimWrapper.Classes.Remove("Closing");
        }

        private async Task PlayOpenAnimationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(16, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            DialogOverlay.Classes.Add("Open");
            DialogAnimWrapper.Classes.Add("Open");

            await Task.Delay(AnimationDurationMs, cancellationToken);
        }

        private async Task PlayCloseAnimationAsync(CancellationToken cancellationToken)
        {
            DialogAnimWrapper.Classes.Add("Closing");

            await Task.Delay(AnimationDurationMs, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            DialogAnimWrapper.Classes.Remove("Closing");
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
                e.Handled = true;
            }
        }

        private void OnDialogBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.CurrentDialog != null)
            {
                vm.CurrentDialog.CloseOnBackgroundClickCommand?.Execute(null);
                e.Handled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _dialogService.OnDialogAnimationRequested -= OnDialogAnimationRequested;

            lock (_animationLock)
            {
                _animationCts?.Cancel();
                _animationCts?.Dispose();
                _animationCts = null;
            }

            _platformService.Cleanup();
            _notificationService.Dispose(); 

            base.OnClosed(e);
        }
    }
}