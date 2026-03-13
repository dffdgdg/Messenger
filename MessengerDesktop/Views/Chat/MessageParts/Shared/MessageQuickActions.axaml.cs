using System.Windows.Input;

namespace MessengerDesktop.Views.Chat.MessageParts.Shared;

public partial class MessageQuickActions : UserControl
{
    public MessageQuickActions() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> ReplyCommandProperty =
        AvaloniaProperty.Register<MessageQuickActions, ICommand?>(nameof(ReplyCommand));

    public static readonly StyledProperty<ICommand?> ForwardCommandProperty =
        AvaloniaProperty.Register<MessageQuickActions, ICommand?>(nameof(ForwardCommand));

    public ICommand? ReplyCommand
    {
        get => GetValue(ReplyCommandProperty);
        set => SetValue(ReplyCommandProperty, value);
    }

    public ICommand? ForwardCommand
    {
        get => GetValue(ForwardCommandProperty);
        set => SetValue(ForwardCommandProperty, value);
    }
}