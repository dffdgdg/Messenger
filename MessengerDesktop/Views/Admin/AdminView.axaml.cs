using Avalonia.Markup.Xaml;
using Avalonia.Reactive;

namespace MessengerDesktop.Views;

public partial class AdminView : UserControl
{
    private readonly Grid? _mainGrid;
    private readonly GridSplitter? _gridSplitter;
    private bool _isDragging;
    private bool _forceCompactMode;
    private bool _compactModeWasForced;

    private const double EXPANDED_WIDTH = 260;
    private const double COMPACT_WIDTH = 72;

    private const double SNAP_TO_COMPACT_THRESHOLD = 160;
    private const double SNAP_TO_EXPANDED_THRESHOLD = 140;

    private const double FORCE_COMPACT_ENTER_WIDTH = 900;
    private const double FORCE_COMPACT_EXIT_WIDTH = 1000;

    public static readonly StyledProperty<bool> IsCompactModeProperty =
        AvaloniaProperty.Register<AdminView, bool>(nameof(IsCompactMode));

    public bool IsCompactMode
    {
        get => GetValue(IsCompactModeProperty);
        set => SetValue(IsCompactModeProperty, value);
    }

    public AdminView()
    {
        InitializeComponent();

        _mainGrid = this.FindControl<Grid>("MainGrid");
        _gridSplitter = this.FindControl<GridSplitter>("GridSplitter");

        this.GetObservable(BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ => EvaluateResponsiveLayout()));

        if (_gridSplitter is not null)
        {
            _gridSplitter.DragStarted += OnSplitterDragStarted;
            _gridSplitter.DragCompleted += OnSplitterDragCompleted;
        }
    }

    /// <summary>
    /// Начало перетаскивания — запоминаем состояние
    /// </summary>
    private void OnSplitterDragStarted(object? sender, Avalonia.Input.VectorEventArgs e) => _isDragging = true;

    /// <summary>
    /// Конец перетаскивания — snap к одному из двух состояний
    /// </summary>
    private void OnSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        _isDragging = false;

        if (_forceCompactMode || _mainGrid?.ColumnDefinitions[0] is not ColumnDefinition column)
            return;

        var currentWidth = column.ActualWidth;

        if (IsCompactMode)
        {
            if (currentWidth > SNAP_TO_EXPANDED_THRESHOLD)
                SnapToExpanded(column);
            else
                SnapToCompact(column);
        }
        else
        {
            if (currentWidth < SNAP_TO_COMPACT_THRESHOLD)
                SnapToCompact(column);
            else
                SnapToExpanded(column);
        }
    }

    private void SnapToCompact(ColumnDefinition column)
    {
        column.Width = new GridLength(COMPACT_WIDTH);
        IsCompactMode = true;
    }

    private void SnapToExpanded(ColumnDefinition column)
    {
        column.Width = new GridLength(EXPANDED_WIDTH);
        IsCompactMode = false;
    }

    /// <summary>
    /// Автоматический компакт при узком окне
    /// </summary>
    private void EvaluateResponsiveLayout()
    {
        if (_isDragging)
            return;

        var width = Bounds.Width;
        if (width <= 0)
            return;

        var nextForceCompact = _forceCompactMode
            ? width < FORCE_COMPACT_EXIT_WIDTH
            : width <= FORCE_COMPACT_ENTER_WIDTH;

        _forceCompactMode = nextForceCompact;

        if (_forceCompactMode && !IsCompactMode
            && _mainGrid?.ColumnDefinitions[0] is ColumnDefinition compactCol)
        {
            SnapToCompact(compactCol);
            _compactModeWasForced = true;
        }
        else if (!_forceCompactMode && _compactModeWasForced
                 && _mainGrid?.ColumnDefinitions[0] is ColumnDefinition expandedCol)
        {
            SnapToExpanded(expandedCol);
            _compactModeWasForced = false;
        }
    }

    public void ToggleCompactMode()
    {
        if (_forceCompactMode)
            return;

        if (_mainGrid?.ColumnDefinitions[0] is not ColumnDefinition column)
            return;

        if (IsCompactMode)
            SnapToExpanded(column);
        else
            SnapToCompact(column);

        _compactModeWasForced = false;
    }

    private void OnExpandButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ToggleCompactMode();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EvaluateResponsiveLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}