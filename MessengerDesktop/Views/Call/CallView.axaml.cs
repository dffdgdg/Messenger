using Avalonia.Reactive;
using MessengerDesktop.ViewModels.Call;
using System;

namespace MessengerDesktop.Views.Call;

public partial class CallView : UserControl
{
    private const double HIDE_PANEL_THRESHOLD = 1020;
    private const double SHOW_PANEL_THRESHOLD = 1100;

    private bool _hiddenByWidth;
    private Border? _sidePanel;
    private CallViewModel? _currentViewModel;

    public CallView()
    {
        InitializeComponent();

        _sidePanel = this.FindControl<Border>("SidePanel");

        DataContextChanged += OnDataContextChanged;
        this.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => EvaluateResponsiveLayout()));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _currentViewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _currentViewModel = DataContext as CallViewModel;

        if (_currentViewModel == null) return;

        _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSidePanelVisibility(_currentViewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CallViewModel.IsChatPanelOpen) &&
            DataContext is CallViewModel vm)
        {
            UpdateSidePanelVisibility(vm);
        }
    }

    private void EvaluateResponsiveLayout()
    {
        var width = Bounds.Width;
        if (width <= 0) return;

        bool nextHidden = _hiddenByWidth ? width < SHOW_PANEL_THRESHOLD : width <= HIDE_PANEL_THRESHOLD;

        if (nextHidden == _hiddenByWidth) return;

        _hiddenByWidth = nextHidden;

        if (DataContext is CallViewModel vm)
            UpdateSidePanelVisibility(vm);
    }

    private void UpdateSidePanelVisibility(CallViewModel vm)
    {
        _sidePanel ??= this.FindControl<Border>("SidePanel");
        if (_sidePanel == null) return;

        _sidePanel.IsVisible = vm.IsChatPanelOpen && !_hiddenByWidth;
    }
}