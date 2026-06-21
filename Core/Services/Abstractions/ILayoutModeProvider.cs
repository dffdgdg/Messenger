using Core.Infrastructure;

namespace Core.Services.Abstractions;

public interface ILayoutModeProvider
{
    LayoutMode LayoutMode { get; }
    IObservable<LayoutMode> LayoutModeChanged { get; }
    IObservable<Rect> WindowBoundsChanged { get; }
}