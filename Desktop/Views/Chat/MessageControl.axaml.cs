using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels.Chat;
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
    public static readonly DirectProperty<MessageControl, bool> IsUltraCompactProperty =
    AvaloniaProperty.RegisterDirect<MessageControl, bool>(
        nameof(IsUltraCompact), o => o.IsUltraCompact);

    private bool _isUltraCompact;
    public bool IsUltraCompact
    {
        get => _isUltraCompact;
        private set => SetAndRaise(IsUltraCompactProperty, ref _isUltraCompact, value);
    }

    private IDisposable? _layoutSub;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var chatView = this.FindAncestorOfType<ChatView>();
        if (chatView != null)
        {
            IsUltraCompact = chatView.LayoutMode == LayoutMode.UltraCompact;
            _layoutSub = chatView.GetObservable(ChatView.LayoutModeProperty)
                .Subscribe(new AnonymousObserver<LayoutMode>(m => IsUltraCompact = m == LayoutMode.UltraCompact));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutSub?.Dispose();
        _lastVm?.PropertyChanged -= OnViewModelPropertyChanged;
        _lastVm = null;

        _bubbleBorder = null;
        _lastBubbleClasses = null;
    }
}