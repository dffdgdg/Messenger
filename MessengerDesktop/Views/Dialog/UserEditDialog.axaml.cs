using Avalonia.Markup.Xaml;

namespace MessengerDesktop.Views;

public partial class UserEditDialog : UserControl
{
    public UserEditDialog() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}