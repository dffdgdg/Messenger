using Avalonia.Controls;
using Avalonia.Input;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels;

namespace MessengerDesktop.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NotificationService.Initialize(this);
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
                e.Handled = true;
            }
        }

        private void OnDialogBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.CurrentDialog != null)
            {
                vm.CurrentDialog.CloseOnBackgroundClickCommand?.Execute(null);
                e.Handled = true;
            }
        }
    }
}