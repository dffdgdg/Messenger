using System.Windows.Input;

namespace MessengerDesktop.Views;

public partial class ChatInfoPanel : UserControl
{
    public ChatInfoPanel() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> OpenProfileCommandProperty =
        AvaloniaProperty.Register<ChatInfoPanel, ICommand?>(nameof(OpenProfileCommand));

    public ICommand? OpenProfileCommand
    {
        get => GetValue(OpenProfileCommandProperty);
        set => SetValue(OpenProfileCommandProperty, value);
    }
}