using Core.Infrastructure;

namespace Core.Services.Platform.Abstractions;

public interface ILayoutModeProvider
{
    LayoutMode LayoutMode { get; }
    IObservable<LayoutMode> LayoutModeChanged { get; }
    IObservable<Rect> WindowBoundsChanged { get; }
}