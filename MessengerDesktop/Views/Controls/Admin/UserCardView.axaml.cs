using System.Windows.Input;

namespace MessengerDesktop.Views.Controls.Admin;

public partial class UserCardView : UserControl
{
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<UserCardView, ICommand?>(nameof(EditCommand));

    public static readonly StyledProperty<ICommand?> BanCommandProperty =
        AvaloniaProperty.Register<UserCardView, ICommand?>(nameof(BanCommand));

    public ICommand? EditCommand
    {
        get => GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public ICommand? BanCommand
    {
        get => GetValue(BanCommandProperty);
        set => SetValue(BanCommandProperty, value);
    }

    public UserCardView() => InitializeComponent();
}