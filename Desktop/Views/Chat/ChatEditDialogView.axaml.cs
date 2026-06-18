using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels.Dialog;

namespace Desktop.Views;

public partial class ChatEditDialog : UserControl
{
    private IDisposable? _layoutModeSub;

    public ChatEditDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChatEditDialogViewModel vm)
            _ = vm.InitializeCommand.ExecuteAsync(null);

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

        // Находим элементы
        var dialogContainer = this.FindControl<Border>("DialogContainer");
        var compactHeader = this.FindControl<Grid>("CompactHeader");
        var normalHeader = this.FindControl<Grid>("NormalHeader");
        var compactAvatar = this.FindControl<Border>("CompactAvatarSection");

        if (dialogContainer == null) return;

        if (isCompact)
        {
            // Контейнер на весь экран без скруглений
            dialogContainer.Classes.Remove("DialogContainer");
            dialogContainer.CornerRadius = new CornerRadius(0);
            dialogContainer.BoxShadow = new BoxShadows();

            // Переключаем шапки
            if (compactHeader != null) compactHeader.IsVisible = true;
            if (normalHeader != null) normalHeader.IsVisible = false;
            if (compactAvatar != null) compactAvatar.IsVisible = true;
        }
        else
        {
            dialogContainer.Classes.Add("DialogContainer");
            dialogContainer.CornerRadius = new CornerRadius(16);
            dialogContainer.BoxShadow = new BoxShadows();

            if (compactHeader != null) compactHeader.IsVisible = false;
            if (normalHeader != null) normalHeader.IsVisible = true;
            if (compactAvatar != null) compactAvatar.IsVisible = false;
        }
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialogBaseViewModel vm)
            vm.CancelCommand.Execute(null);
    }
}