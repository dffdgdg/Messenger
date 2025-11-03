using Avalonia.Controls;
using Avalonia.Input;
using MessengerDesktop.Services;

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
    }
}