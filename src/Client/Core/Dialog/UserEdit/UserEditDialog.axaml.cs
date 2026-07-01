using Avalonia.Markup.Xaml;

namespace Core.Dialog.UserEdit;

public partial class UserEditDialog : UserControl
{
    public UserEditDialog() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}