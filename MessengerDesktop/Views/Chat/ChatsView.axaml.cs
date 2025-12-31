using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MessengerDesktop.Views
{
    public partial class ChatsView : UserControl
    {
        private readonly Grid? _mainGrid;
        private bool _isDragging = false;

        private const double COMPACT_WIDTH = 72;
        private const double ENTER_COMPACT_THRESHOLD = 120;
        private const double EXIT_COMPACT_THRESHOLD = 160;
        private const double NORMAL_DEFAULT_WIDTH = 280;
        private const double MIN_WIDTH = 72;
        private const double MAX_WIDTH = 400;

        private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;

        public static readonly StyledProperty<bool> IsCompactModeProperty =
            AvaloniaProperty.Register<ChatsView, bool>(nameof(IsCompactMode));

        public bool IsCompactMode
        {
            get => GetValue(IsCompactModeProperty);
            set => SetValue(IsCompactModeProperty, value);
        }

        public ChatsView()
        {
            InitializeComponent();

            _chatInfoPanelStateStore = App.Current.Services.GetRequiredService<IChatInfoPanelStateStore>();
            _mainGrid = this.FindControl<Grid>("MainGrid");

            this.DataContextChanged += ChatsView_DataContextChanged;

            if (_mainGrid != null)
            {
                var column = _mainGrid.ColumnDefinitions[0];
                var splitter = this.FindControl<GridSplitter>("GridSplitter");

                if (column != null)
                {
                    column.MinWidth = MIN_WIDTH;
                    column.MaxWidth = MAX_WIDTH;

                    column.PropertyChanged += ChatListColumn_PropertyChanged;
                }

                if (splitter != null)
                {
                    splitter.DragStarted += Splitter_DragStarted;
                    splitter.DragCompleted += Splitter_DragCompleted;
                }
            }
        }

        private void ChatsView_DataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is ChatsViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
                UpdateInfoPanelVisibility(vm);
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (DataContext is not ChatsViewModel vm) return;
            if (e.PropertyName == nameof(ChatsViewModel.CurrentChatViewModel) ||
                e.PropertyName == nameof(ChatsViewModel.CombinedIsInfoPanelVisible))
            {
                UpdateInfoPanelVisibility(vm);
            }
        }

        private void UpdateInfoPanelVisibility(ChatsViewModel vm)
        {
            var panel = this.FindControl<Control>("InfoPanel");
            if (panel == null) return;

            var isAnyChatSelected = vm.CurrentChatViewModel != null;
            var globalOpen = _chatInfoPanelStateStore.IsOpen;

            panel.IsVisible = isAnyChatSelected && globalOpen;
        }

        private void Splitter_DragStarted(object? sender, VectorEventArgs e) => _isDragging = true;

        private void Splitter_DragCompleted(object? sender, VectorEventArgs e)
        {
            _isDragging = false;

            if (_mainGrid?.ColumnDefinitions[0] is ColumnDefinition column)
            {
                double currentWidth = column.Width.Value;

                if (currentWidth <= ENTER_COMPACT_THRESHOLD && !IsCompactMode)
                {
                    column.Width = new GridLength(COMPACT_WIDTH);
                    IsCompactMode = true;
                }
                else if (currentWidth >= EXIT_COMPACT_THRESHOLD && IsCompactMode)
                {
                    IsCompactMode = false;
                }
                else if (IsCompactMode && currentWidth < EXIT_COMPACT_THRESHOLD)
                {
                    column.Width = new GridLength(COMPACT_WIDTH);
                }
            }
        }

        private void ChatListColumn_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!_isDragging || sender is not ColumnDefinition column || e.Property != ColumnDefinition.WidthProperty)
                return;

            double currentWidth = column.Width.Value;

            if (currentWidth <= ENTER_COMPACT_THRESHOLD && !IsCompactMode)
            {
                IsCompactMode = true;
            }
            else if (currentWidth >= EXIT_COMPACT_THRESHOLD && IsCompactMode)
            {
                IsCompactMode = false;
            }
        }

        public void ToggleCompactMode()
        {
            if (_mainGrid?.ColumnDefinitions[0] is ColumnDefinition column)
            {
                if (IsCompactMode)
                {
                    column.Width = new GridLength(NORMAL_DEFAULT_WIDTH);
                    IsCompactMode = false;
                }
                else
                {
                    column.Width = new GridLength(COMPACT_WIDTH);
                    IsCompactMode = true;
                }
            }
        }

        public void ExpandFromCompact()
        {
            if (IsCompactMode)
            {
                ToggleCompactMode();
            }
        }

        private void OnExpandButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ToggleCompactMode();

        private void OnSearchButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ExpandFromCompact();

            Dispatcher.UIThread.Post(() =>
            {
                var searchBox = this.FindControl<TextBox>("SearchTextBox");
                searchBox?.Focus();
            }, DispatcherPriority.Background);
        }

        private void OnProfileBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is ChatsViewModel vm && vm.UserProfileDialog != null)
            {
                vm.UserProfileDialog = null;
                e.Handled = true;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (_mainGrid?.ColumnDefinitions[0] is ColumnDefinition column)
            {
                double width = column.Width.Value;
                IsCompactMode = width <= ENTER_COMPACT_THRESHOLD;
            }
        }
    }
}