using Avalonia.Markup.Xaml;

namespace Core.Views.Controls.Skeleton;

public partial class DepartmentMembersSkeleton : UserControl
{
    public DepartmentMembersSkeleton() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}