using System.Windows.Input;

namespace MessengerDesktop.Views.Chat.MessageParts;

public partial class VoiceMessagePart : UserControl
{
    public VoiceMessagePart() => InitializeComponent();

    public static readonly StyledProperty<ICommand?> RetryTranscriptionCommandProperty =
        AvaloniaProperty.Register<VoiceMessagePart, ICommand?>(nameof(RetryTranscriptionCommand));

    public ICommand? RetryTranscriptionCommand
    {
        get => GetValue(RetryTranscriptionCommandProperty);
        set => SetValue(RetryTranscriptionCommandProperty, value);
    }
}