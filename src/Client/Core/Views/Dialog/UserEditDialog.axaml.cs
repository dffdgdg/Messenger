using Avalonia.Markup.Xaml;

namespace Core.Views;

public partial class UserEditDialog : UserControl
{
    public UserEditDialog() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}