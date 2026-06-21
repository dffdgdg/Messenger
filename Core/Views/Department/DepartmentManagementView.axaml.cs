using Avalonia.Reactive;
using Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Views.Department;

public partial class DepartmentManagementView : UserControl
{
    public DepartmentManagementView() => InitializeComponent();
    public static readonly DirectProperty<DepartmentManagementView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<DepartmentManagementView, LayoutMode>(nameof(LayoutMode), o => o.LayoutMode);

    private LayoutMode _layoutMode;
    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        private set => SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
    }

    private IDisposable? _layoutModeSubscription;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var layoutProvider = AppConfig.Services.GetService<ILayoutModeProvider>();

        if (layoutProvider is null) return;

        LayoutMode = layoutProvider.LayoutMode;

        _layoutModeSubscription = layoutProvider.LayoutModeChanged.Subscribe(new AnonymousObserver<LayoutMode>(m => LayoutMode = m));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutModeSubscription?.Dispose();
    }
}