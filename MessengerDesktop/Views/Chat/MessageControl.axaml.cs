using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace MessengerDesktop.Views.Chat;

public partial class MessageControl : UserControl
{
    public MessageControl() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> EditMessageCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(EditMessageCommand));

    public static readonly StyledProperty<ICommand?> CopyTextCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(CopyTextCommand));

    public static readonly StyledProperty<ICommand?> DeleteMessageCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(DeleteMessageCommand));

    public static readonly StyledProperty<ICommand?> OpenProfileCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(OpenProfileCommand));

    public static readonly StyledProperty<ICommand?> ReplyCommandProperty =
       AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(ReplyCommand));

    public static readonly StyledProperty<ICommand?> ScrollToReplyCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(ScrollToReplyCommand));

    public static readonly StyledProperty<ICommand?> RetryTranscriptionCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(RetryTranscriptionCommand));

    public static readonly StyledProperty<ICommand?> ForwardCommandProperty =
        AvaloniaProperty.Register<MessageControl, ICommand?>(nameof(ForwardCommand));

    public ICommand? EditMessageCommand
    {
        get => GetValue(EditMessageCommandProperty);
        set => SetValue(EditMessageCommandProperty, value);
    }

    public ICommand? CopyTextCommand
    {
        get => GetValue(CopyTextCommandProperty);
        set => SetValue(CopyTextCommandProperty, value);
    }

    public ICommand? DeleteMessageCommand
    {
        get => GetValue(DeleteMessageCommandProperty);
        set => SetValue(DeleteMessageCommandProperty, value);
    }

    public ICommand? OpenProfileCommand
    {
        get => GetValue(OpenProfileCommandProperty);
        set => SetValue(OpenProfileCommandProperty, value);
    }

    public ICommand? ReplyCommand
    {
        get => GetValue(ReplyCommandProperty);
        set => SetValue(ReplyCommandProperty, value);
    }

    public ICommand? ScrollToReplyCommand
    {
        get => GetValue(ScrollToReplyCommandProperty);
        set => SetValue(ScrollToReplyCommandProperty, value);
    }

    public ICommand? RetryTranscriptionCommand
    {
        get => GetValue(RetryTranscriptionCommandProperty);
        set => SetValue(RetryTranscriptionCommandProperty, value);
    }

    public ICommand? ForwardCommand
    {
        get => GetValue(ForwardCommandProperty);
        set => SetValue(ForwardCommandProperty, value);
    }
}