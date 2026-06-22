using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Views.Dialog;

public partial class UserListDialog : UserControl
{
    private IDisposable? _layoutModeSub;

    public UserListDialog() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var layoutProvider = AppConfig.Services.GetService<ILayoutModeProvider>();
        if (layoutProvider != null)
        {
            AdaptToLayoutMode(layoutProvider.LayoutMode);
            _layoutModeSub = layoutProvider.LayoutModeChanged.Subscribe(new AnonymousObserver<LayoutMode>(AdaptToLayoutMode));
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _layoutModeSub?.Dispose();
    }

    private void AdaptToLayoutMode(LayoutMode mode)
    {
        var isCompact = mode is LayoutMode.UltraCompact or LayoutMode.Compact;

        var dialogContainer = this.FindControl<Border>("DialogContainer");
        var compactHeader = this.FindControl<Grid>("CompactHeader");
        var normalHeader = this.FindControl<Grid>("NormalHeader");

        if (dialogContainer == null) return;

        if (isCompact)
        {
            dialogContainer.Classes.Remove("DialogContainer");
            dialogContainer.CornerRadius = new CornerRadius(0);
            dialogContainer.BoxShadow = new BoxShadows();

            if (compactHeader != null) compactHeader.IsVisible = true;
            if (normalHeader != null) normalHeader.IsVisible = false;
        }
        else
        {
            dialogContainer.Classes.Add("DialogContainer");
            dialogContainer.CornerRadius = new CornerRadius(16);
            dialogContainer.BoxShadow = new BoxShadows();

            if (compactHeader != null) compactHeader.IsVisible = false;
            if (normalHeader != null) normalHeader.IsVisible = true;
        }
    }

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UserListDialogViewModel vm || !vm.IsEditMode || !vm.AllowEdit)
            return;

        if (sender is not Border { DataContext: UserListItemViewModel user })
            return;

        user.IsSelected = !user.IsSelected;
        e.Handled = true;
    }
}