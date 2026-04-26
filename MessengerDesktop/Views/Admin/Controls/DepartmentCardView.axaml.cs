using System.Windows.Input;

namespace MessengerDesktop.Views.Controls.Admin;

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