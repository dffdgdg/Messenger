using Avalonia.Input;
using Avalonia.Interactivity;

namespace MessengerDesktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (this.FindControl<TextBox>("PasswordBox") is { } passwordBox)
            passwordBox.KeyDown += OnPasswordKeyDown;
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