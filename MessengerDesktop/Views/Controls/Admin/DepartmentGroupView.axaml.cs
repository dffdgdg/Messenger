using System.Windows.Input;

namespace MessengerDesktop.Views.Controls.Admin;

public partial class DepartmentGroupView : UserControl
{
    public static readonly StyledProperty<ICommand?> EditUserCommandProperty =
        AvaloniaProperty.Register<DepartmentGroupView, ICommand?>(nameof(EditUserCommand));

    public static readonly StyledProperty<ICommand?> BanUserCommandProperty =
        AvaloniaProperty.Register<DepartmentGroupView, ICommand?>(nameof(BanUserCommand));

    public ICommand? EditUserCommand
    {
        get => GetValue(EditUserCommandProperty);
        set => SetValue(EditUserCommandProperty, value);
    }

    public ICommand? BanUserCommand
    {
        get => GetValue(BanUserCommandProperty);
        set => SetValue(BanUserCommandProperty, value);
    }

    public DepartmentGroupView() => InitializeComponent();
}