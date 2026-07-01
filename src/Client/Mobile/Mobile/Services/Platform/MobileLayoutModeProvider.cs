using Avalonia;
using Core.Infrastructure;
using Core.Services.Platform.Abstractions;
using Mobile.Views;
using System;

namespace Mobile.Services.Platform;

public class MobileLayoutModeProvider(MainView View) : ILayoutModeProvider
{
    public LayoutMode LayoutMode => View.LayoutMode;

    public IObservable<LayoutMode> LayoutModeChanged
        => View.GetObservable(MainView.LayoutModeProperty);

    public IObservable<Rect> WindowBoundsChanged
        => View.GetObservable(MainView.BoundsProperty);
}