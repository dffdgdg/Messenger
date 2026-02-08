using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessengerDesktop.ViewModels;

namespace MessengerDesktop.Views
{
    public partial class LoginView : UserControl
    {
        public LoginView() => InitializeComponent();
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            // Подписка на Enter в поле пароля
            if (this.FindControl<TextBox>("PasswordBox") is { } passwordBox)
            {
                passwordBox.KeyDown += OnPasswordKeyDown;
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (this.FindControl<TextBox>("PasswordBox") is { } passwordBox)
            {
                passwordBox.KeyDown -= OnPasswordKeyDown;
            }

            base.OnUnloaded(e);
        }

        private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is LoginViewModel vm && vm.LoginCommand.CanExecute(null))
            {
                vm.LoginCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}