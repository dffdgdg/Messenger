using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MessengerDesktop.Views;

public partial class DepartmentDialog : UserControl
{
    public DepartmentDialog() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}