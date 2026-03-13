using System.Windows.Input;

namespace MessengerDesktop.Views.Chat.MessageParts.Shared;

public partial class ReplyPreviewBlock : UserControl
{
    public ReplyPreviewBlock() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> ScrollToReplyCommandProperty =
        AvaloniaProperty.Register<ReplyPreviewBlock, ICommand?>(nameof(ScrollToReplyCommand));

    public ICommand? ScrollToReplyCommand
    {
        get => GetValue(ScrollToReplyCommandProperty);
        set => SetValue(ScrollToReplyCommandProperty, value);
    }
}