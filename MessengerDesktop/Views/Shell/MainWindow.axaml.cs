using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Views;

public partial class MainWindow : Window
{
    private readonly IDialogService _dialogService;
    private readonly IPlatformService _platformService;
    private readonly INotificationService _notificationService;

    private const int AnimationDurationMs = 250;
    private const int FrameDelayMs = 16;
    private const double MaximizedPadding = 7;
    private const string OpenClass = "Open";
    private const string ClosingClass = "Closing";

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

        UpdateWindowPadding();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateWindowPadding();
        }
    }

    private void UpdateWindowPadding() =>
        Padding = WindowState == WindowState.Maximized
            ? new Thickness(MaximizedPadding)
            : default;

    private void OnDialogAnimationRequested(bool isOpening)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            bool completed = false;

            try
            {
                completed = await RunAnimationAsync(isOpening);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dialog animation error: {ex}");
                ResetAnimationState();
            }
            finally
            {
                if (completed)
                {
                    _dialogService.NotifyAnimationComplete();
                }
            }
        });
    }

    private async Task<bool> RunAnimationAsync(bool isOpening)
    {
        CancellationTokenSource? oldCts;
        var newCts = new CancellationTokenSource();

        lock (_animationLock)
        {
            oldCts = _animationCts;
            _animationCts = newCts;
        }

        if (oldCts is not null)
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }

        try
        {
            if (isOpening)
            {
                ResetAnimationState();
                await PlayOpenAnimationAsync(newCts.Token);
            }
            else
            {
                await PlayCloseAnimationAsync(newCts.Token);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            ResetAnimationState();
            return false;
        }
    }

    private void ResetAnimationState()
    {
        DialogOverlay.Classes.Remove(OpenClass);
        DialogOverlay.Classes.Remove(ClosingClass);
        DialogAnimWrapper.Classes.Remove(OpenClass);
        DialogAnimWrapper.Classes.Remove(ClosingClass);
    }

    private async Task PlayOpenAnimationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(FrameDelayMs, cancellationToken);

        DialogOverlay.Classes.Add(OpenClass);
        DialogAnimWrapper.Classes.Add(OpenClass);

        await Task.Delay(AnimationDurationMs, cancellationToken);
    }

    private async Task PlayCloseAnimationAsync(CancellationToken cancellationToken)
    {
        DialogOverlay.Classes.Remove(OpenClass);
        DialogAnimWrapper.Classes.Remove(OpenClass);
        DialogAnimWrapper.Classes.Add(ClosingClass);

        await Task.Delay(AnimationDurationMs, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        DialogAnimWrapper.Classes.Remove(ClosingClass);
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
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentDialog?.CloseOnBackgroundClickCommand?.Execute(null);
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