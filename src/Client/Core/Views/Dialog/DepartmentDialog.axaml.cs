using Avalonia.Markup.Xaml;

namespace Core.Views;

public partial class DepartmentDialog : UserControl
{
    public DepartmentDialog() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}