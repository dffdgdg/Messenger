using System.Windows.Input;

namespace Core.Features.Chat.Views.MessageParts;

public partial class ReplyPreViewBlock : UserControl
{
    public ReplyPreViewBlock() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> ScrollToReplyCommandProperty =
        AvaloniaProperty.Register<ReplyPreViewBlock, ICommand?>(nameof(ScrollToReplyCommand));

    public ICommand? ScrollToReplyCommand
    {
        get => GetValue(ScrollToReplyCommandProperty);
        set => SetValue(ScrollToReplyCommandProperty, value);
    }
}