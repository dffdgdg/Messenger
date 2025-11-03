using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels;
using System;

namespace MessengerDesktop.Views
{
    public partial class ChatsView : UserControl
    {
        private readonly Grid? _chatListGrid;
        private readonly Grid? _mainGrid;
        private bool _isDragging = false;
        private const double COMPACT_WIDTH = 75;
        private const double COMPACT_THRESHOLD = 200;

        public ChatsView()
        {
            InitializeComponent();
            
            _chatListGrid = this.FindControl<Grid>("ChatListGrid");
            _mainGrid = this.FindControl<Grid>("MainGrid");

            this.DataContextChanged += ChatsView_DataContextChanged;

            if (_mainGrid != null)
            {
                var column = _mainGrid.ColumnDefinitions[0];
                var splitter = this.FindControl<GridSplitter>("GridSplitter");
                
                if (column != null)
                {
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
            if (e.PropertyName == nameof(ChatsViewModel.CurrentChatViewModel) || e.PropertyName == nameof(ChatsViewModel.CombinedIsInfoPanelVisible))
            {
                UpdateInfoPanelVisibility(vm);
            }
        }

        private void UpdateInfoPanelVisibility(ChatsViewModel vm)
        {
            var panel = this.FindControl<Control>("InfoPanel");
            if (panel == null) return;

            var isAnyChatSelected = vm.CurrentChatViewModel != null;
            var globalOpen = ChatInfoPanelStateStore.Get();

            panel.IsVisible = isAnyChatSelected && globalOpen;
        }

        private void Splitter_DragStarted(object? sender, VectorEventArgs e) => _isDragging = true;

        private void Splitter_DragCompleted(object? sender, VectorEventArgs e) => _isDragging = false;

        private void ChatListColumn_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!_isDragging || sender is not ColumnDefinition column || e.Property != ColumnDefinition.WidthProperty)
                return;

            double currentWidth = column.Width.Value;

            if (currentWidth < COMPACT_THRESHOLD)
            {
                column.Width = new GridLength(COMPACT_WIDTH);
                UpdateChatItemsOpacity(0);
            }
            else
            {
                UpdateChatItemsOpacity(1);
            }
        }

        private void UpdateChatItemsOpacity(double opacity)
        {
            if (_chatListGrid == null) return;

            foreach (var item in _chatListGrid.GetVisualDescendants())
            {
                if (item is Grid grid && grid.Name == "ChatDetails")
                {
                    grid.Opacity = opacity;
                    grid.IsVisible = opacity > 0;
                }
            }
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
                double currentWidth = column.Width.Value;
                UpdateChatItemsOpacity(currentWidth < COMPACT_THRESHOLD ? 0 : 1);
            }
        }
    }
}