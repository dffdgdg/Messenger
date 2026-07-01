using Core.Infrastructure;
using Core.Services.Platform.Abstractions;
using Desktop.Views;

namespace Desktop.Shared.Services.Platform;

public class DesktopLayoutModeProvider(MainWindow window) : ILayoutModeProvider
{
    public LayoutMode LayoutMode => window.LayoutMode;

    public IObservable<LayoutMode> LayoutModeChanged
        => window.GetObservable(MainWindow.LayoutModeProperty);

    public IObservable<Rect> WindowBoundsChanged
        => window.GetObservable(MainWindow.BoundsProperty);
}