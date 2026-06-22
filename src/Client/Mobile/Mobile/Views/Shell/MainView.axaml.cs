using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.Views;
using System;

namespace Mobile.Views;

public partial class MainView : UserControl
{
    public static readonly DirectProperty<MainView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<MainView, LayoutMode>(
            nameof(LayoutMode), o => o.LayoutMode);

    private LayoutMode _layoutMode = LayoutMode.Normal;
    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        private set => SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
    }

    private const double UltraCompactThreshold = 500;
    private const double CompactThreshold = 700;
    private const double WideThreshold = 900;

    private IDisposable? _boundsSub;

    public MainView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _boundsSub = this.GetObservable(BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ =>
            {
                UpdateLayoutMode();
                PushLayoutModeToMainMenu();
            }));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _boundsSub?.Dispose();
    }

    private void UpdateLayoutMode()
    {
        var w = Bounds.Width;
        LayoutMode = w < UltraCompactThreshold ? LayoutMode.UltraCompact
                   : w < CompactThreshold ? LayoutMode.Compact
                   : w < WideThreshold ? LayoutMode.Normal : LayoutMode.Wide;
    }

    private void PushLayoutModeToMainMenu()
        => this.FindDescendantOfType<MainMenuView>()?.WindowLayoutMode = LayoutMode;
}