using Core.Infrastructure;

namespace Core.Views;

public partial class MainMenuView : UserControl
{
    public static readonly DirectProperty<MainMenuView, LayoutMode> WindowLayoutModeProperty =
        AvaloniaProperty.RegisterDirect<MainMenuView, LayoutMode>(nameof(WindowLayoutMode), o => o.WindowLayoutMode, (o, v) => o.WindowLayoutMode = v);

    private LayoutMode _windowLayoutMode = LayoutMode.Normal;

    public LayoutMode WindowLayoutMode
    {
        get => _windowLayoutMode;
        set => SetAndRaise(WindowLayoutModeProperty, ref _windowLayoutMode, value);
    }

    public MainMenuView() => InitializeComponent();
}