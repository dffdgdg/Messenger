using Avalonia.Markup.Xaml;

namespace Desktop.Views.Controls.Skeleton;

public partial class DepartmentMembersSkeleton : UserControl
{
    public DepartmentMembersSkeleton() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}