using Avalonia.Markup.Xaml;

namespace Desktop.Views;

public partial class UserEditDialog : UserControl
{
    public UserEditDialog() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}