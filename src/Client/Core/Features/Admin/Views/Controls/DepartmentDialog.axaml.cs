using Avalonia.Markup.Xaml;

namespace Core.Features.Admin.Views.Controls;

public partial class DepartmentDialog : UserControl
{
    public DepartmentDialog() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}