using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels.Dialog;

namespace Desktop.Views;

public partial class PollDialog : UserControl
{
    private IDisposable? _layoutModeSub;

    public PollDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window is MainWindow mainWindow)
        {
            AdaptToLayoutMode(mainWindow.LayoutMode);
            _layoutModeSub = mainWindow
                .GetObservable(MainWindow.LayoutModeProperty)
                .Subscribe(new AnonymousObserver<LayoutMode>(AdaptToLayoutMode));
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _layoutModeSub?.Dispose();
    }

    private void AdaptToLayoutMode(LayoutMode mode)
    {
        var isCompact = mode is LayoutMode.UltraCompact or LayoutMode.Compact;
        var container = this.FindControl<Border>("DialogContainer");
        var compactHeader = this.FindControl<Grid>("CompactHeader");
        var normalHeader = this.FindControl<Grid>("NormalHeader");

        if (container == null) return;

        if (isCompact)
        {
            container.Classes.Remove("DialogContainer");
            container.CornerRadius = new CornerRadius(0);
            container.BoxShadow = new BoxShadows();
            if (compactHeader != null) compactHeader.IsVisible = true;
            if (normalHeader != null) normalHeader.IsVisible = false;
        }
        else
        {
            container.Classes.Add("DialogContainer");
            if (compactHeader != null) compactHeader.IsVisible = false;
            if (normalHeader != null) normalHeader.IsVisible = true;
        }
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialogBaseViewModel vm)
            vm.CancelCommand.Execute(null);
    }
}