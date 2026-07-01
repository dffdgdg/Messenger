namespace Core.Shared.Controls.Skeleton;

public partial class ChatInfoSkeleton : UserControl
{
    public static readonly StyledProperty<bool> IsContactChatProperty =
        AvaloniaProperty.Register<ChatInfoSkeleton, bool>(nameof(IsContactChat), true);

    public bool IsContactChat
    {
        get => GetValue(IsContactChatProperty);
        set => SetValue(IsContactChatProperty, value);
    }

    public ChatInfoSkeleton() => InitializeComponent();
}