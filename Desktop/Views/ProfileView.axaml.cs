using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;

namespace Desktop.Views;

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

        var window = this.FindAncestorOfType<Window>();
        if (window is MainWindow mainWindow)
        {
            LayoutMode = mainWindow.LayoutMode;
            _layoutModeSubscription = mainWindow
                .GetObservable(MainWindow.LayoutModeProperty)
                .Subscribe(new AnonymousObserver<LayoutMode>(m => LayoutMode = m));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutModeSubscription?.Dispose();
    }
}