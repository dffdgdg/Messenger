using Avalonia.Markup.Xaml;

namespace Desktop.Views;

public partial class DepartmentDialog : UserControl
{
    public DepartmentDialog() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}