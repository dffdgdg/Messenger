using Core.Services.Platform.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Features.Shell.MainMenu.ViewModels.Navigation;

public class NavigationOrchestrator
{
    private readonly Stack<int> _backHistory = [], _forwardHistory = [];
    private readonly IDrawerService _drawerService;

    public int SelectedMenuIndex { get; private set; } = 1;
    public bool CanGoBack => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;

    public event Action<int>? IndexChanged;

    public NavigationOrchestrator(IDrawerService drawerService) => _drawerService = drawerService;

    public void NavigateTo(int index, bool addToHistory)
    {
        if (addToHistory && SelectedMenuIndex != index)
        {
            _backHistory.Push(SelectedMenuIndex);
            _forwardHistory.Clear();
        }

        SelectedMenuIndex = index;
        _drawerService.CloseDrawer();
        IndexChanged?.Invoke(index);
    }

    public bool GoBack()
    {
        if (!CanGoBack) return false;
        var prev = _backHistory.Pop();
        if (SelectedMenuIndex != prev) _forwardHistory.Push(SelectedMenuIndex);
        NavigateTo(prev, false);
        return true;
    }

    public bool GoForward()
    {
        if (!CanGoForward) return false;
        var next = _forwardHistory.Pop();
        if (SelectedMenuIndex != next) _backHistory.Push(SelectedMenuIndex);
        NavigateTo(next, false);
        return true;
    }
}
