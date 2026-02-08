using Avalonia;
using Avalonia.Controls;
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
}