using Avalonia.Controls;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels;

namespace Desktop.Views;

public partial class MainView : UserControl
{
    public static readonly DirectProperty<MainView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<MainView, LayoutMode>(
            nameof(LayoutMode),
            o => o.LayoutMode);

    private LayoutMode _layoutMode = LayoutMode.Normal;
    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        private set => SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
    }

    private const double UltraCompactThreshold = 500;
    private const double CompactThreshold = 700;
    private const double WideThreshold = 900;

    public MainView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        this.GetObservable(BoundsProperty)
            .Subscribe(new Avalonia.Reactive.AnonymousObserver<Rect>(_ =>
            {
                UpdateLayoutMode();
                PushLayoutModeToMainMenu();
            }));
    }

    private void UpdateLayoutMode()
    {
        var w = Bounds.Width;

        LayoutMode = w < UltraCompactThreshold ? LayoutMode.UltraCompact
                   : w < CompactThreshold ? LayoutMode.Compact
                   : w < WideThreshold ? LayoutMode.Normal
                   : LayoutMode.Wide;
    }

    private void PushLayoutModeToMainMenu()
    {
        var menuView = this.FindDescendantOfType<MainMenuView>();
        menuView?.WindowLayoutMode = LayoutMode;
    }
}