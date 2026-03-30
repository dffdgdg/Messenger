using System.Windows.Input;

namespace MessengerDesktop.Views.Chat.MessageParts;

public partial class SystemMessagePart : UserControl
{
    public SystemMessagePart() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> OpenProfileCommandProperty =
            AvaloniaProperty.Register<SystemMessagePart, ICommand?>(nameof(OpenProfileCommand));

    public ICommand? OpenProfileCommand
    {
        get => GetValue(OpenProfileCommandProperty);
        set => SetValue(OpenProfileCommandProperty, value);
    }
}