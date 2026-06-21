using Avalonia.Reactive;
using Core.Infrastructure;
using Core.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Views;

public partial class ProfileView : UserControl
{
    public ProfileView() => InitializeComponent();

    public static readonly DirectProperty<ProfileView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<ProfileView, LayoutMode>(
            nameof(LayoutMode), o => o.LayoutMode);

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