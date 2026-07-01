using Core.Infrastructure;

namespace Core.Features.Shell.MainMenu;

public partial class MainMenuView : UserControl
{
    public static readonly DirectProperty<MainMenuView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<MainMenuView, LayoutMode>(nameof(LayoutMode), o => o.LayoutMode, (o, v) => o.LayoutMode = v);

    private LayoutMode _layoutMode = LayoutMode.Normal;

    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        set => SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
    }

    public MainMenuView() => InitializeComponent();
}