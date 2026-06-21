using Avalonia;
using Core.Infrastructure;
using Core.Services.Abstractions;
using Mobile.Views;
using System;

namespace Mobile.Services.Platform;

public class MobileLayoutModeProvider(MainView view) : ILayoutModeProvider
{
    public LayoutMode LayoutMode => view.LayoutMode;

    public IObservable<LayoutMode> LayoutModeChanged
        => view.GetObservable(MainView.LayoutModeProperty);

    public IObservable<Rect> WindowBoundsChanged
        => view.GetObservable(MainView.BoundsProperty);
}