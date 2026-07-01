using Avalonia.Interactivity;
using Avalonia.Reactive;
using Core.Dialog.Shared;
using Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Dialog.ChatEdit;

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

        var layoutProvider = AppConfig.Services.GetService<ILayoutModeProvider>();
        if (layoutProvider != null)
        {
            AdaptToLayoutMode(layoutProvider.LayoutMode);
            _layoutModeSub = layoutProvider.LayoutModeChanged.Subscribe(new AnonymousObserver<LayoutMode>(AdaptToLayoutMode));
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => _layoutModeSub?.Dispose();

    private void AdaptToLayoutMode(LayoutMode mode)
    {
        var isCompact = mode is LayoutMode.UltraCompact or LayoutMode.Compact;

        var dialogContainer = this.FindControl<Border>("DialogContainer");
        var compactHeader = this.FindControl<Grid>("CompactHeader");
        var normalHeader = this.FindControl<Grid>("NormalHeader");
        var compactAvatar = this.FindControl<Border>("CompactAvatarSection");

        if (dialogContainer == null) return;

        if (isCompact)
        {
            dialogContainer.Classes.Remove("DialogContainer");
            dialogContainer.CornerRadius = new CornerRadius(0);
            dialogContainer.BoxShadow = new BoxShadows();

            compactHeader?.IsVisible = true;
            normalHeader?.IsVisible = false;
            compactAvatar?.IsVisible = true;
        }
        else
        {
            dialogContainer.Classes.Add("DialogContainer");
            dialogContainer.CornerRadius = new CornerRadius(16);
            dialogContainer.BoxShadow = new BoxShadows();

            compactHeader?.IsVisible = false;
            normalHeader?.IsVisible = true;
            compactAvatar?.IsVisible = false;
        }
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialogBaseViewModel vm)
            vm.CancelCommand.Execute(null);
    }
}