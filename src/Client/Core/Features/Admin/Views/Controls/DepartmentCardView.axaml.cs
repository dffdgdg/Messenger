using System.Windows.Input;

namespace Core.Features.Admin.Views.Controls;

public partial class DepartmentCardView : UserControl
{
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<DepartmentCardView, ICommand?>(nameof(EditCommand));

    public ICommand? EditCommand
    {
        get => GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public DepartmentCardView() => InitializeComponent();
}