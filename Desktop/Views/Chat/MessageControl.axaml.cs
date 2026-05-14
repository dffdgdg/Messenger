using Desktop.ViewModels.Chat;
using System.ComponentModel;

namespace Desktop.Views.Chat;

public partial class MessageControl : UserControl
{
    private Border? _bubbleBorder;
    private string? _lastBubbleClasses;
    private MessageViewModel? _lastVm;

    public MessageControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _lastVm?.PropertyChanged -= OnViewModelPropertyChanged;
        _lastVm = null;

        if (DataContext is MessageViewModel vm)
        {
            _lastVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyBubbleClasses(vm.BubbleClasses);
        }
        else
        {
            ApplyBubbleClasses(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MessageViewModel vm) return;

        if (vm.IsDisposed)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            if (ReferenceEquals(_lastVm, vm))
                _lastVm = null;
            return;
        }

        if (e.PropertyName == nameof(MessageViewModel.BubbleClasses))
            ApplyBubbleClasses(vm.BubbleClasses);
    }

    private void ApplyBubbleClasses(string? newClasses)
    {
        _bubbleBorder ??= this.FindControl<Border>("BubbleBorder");
        if (_bubbleBorder is null) return;

        if (!string.IsNullOrEmpty(_lastBubbleClasses))
        {
            foreach (var cls in _lastBubbleClasses.Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _bubbleBorder.Classes.Remove(cls);
            }
        }

        _lastBubbleClasses = newClasses;

        if (!string.IsNullOrEmpty(newClasses))
        {
            _bubbleBorder.Classes.AddRange(newClasses.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _lastVm?.PropertyChanged -= OnViewModelPropertyChanged;
        _lastVm = null;

        _bubbleBorder = null;
        _lastBubbleClasses = null;
    }
}