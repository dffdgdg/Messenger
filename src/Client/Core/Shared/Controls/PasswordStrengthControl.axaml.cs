namespace Core.Shared.Controls;

public partial class PasswordStrengthControl : UserControl
{
    public static readonly StyledProperty<int> StrengthProperty = AvaloniaProperty.Register<PasswordStrengthControl, int>(nameof(Strength));

    public static readonly StyledProperty<string> StrengthLabelProperty = AvaloniaProperty.Register<PasswordStrengthControl, string>(nameof(StrengthLabel), string.Empty);

    public static readonly StyledProperty<bool> PasswordsMatchProperty = AvaloniaProperty.Register<PasswordStrengthControl, bool>(nameof(PasswordsMatch));

    public static readonly StyledProperty<bool> ShowMatchProperty = AvaloniaProperty.Register<PasswordStrengthControl, bool>(nameof(ShowMatch));

    public int Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public string StrengthLabel
    {
        get => GetValue(StrengthLabelProperty);
        set => SetValue(StrengthLabelProperty, value);
    }

    public bool PasswordsMatch
    {
        get => GetValue(PasswordsMatchProperty);
        set => SetValue(PasswordsMatchProperty, value);
    }

    public bool ShowMatch
    {
        get => GetValue(ShowMatchProperty);
        set => SetValue(ShowMatchProperty, value);
    }

    static PasswordStrengthControl() => StrengthProperty.Changed.AddClassHandler<PasswordStrengthControl>((c, _) => c.UpdateSegmentClasses());

    public PasswordStrengthControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateSegmentClasses();
    }

    private void UpdateSegmentClasses()
    {
        if (Seg1 is null || Seg2 is null || Seg3 is null || Seg4 is null) return;

        SetSegment(Seg1, Strength, 1);
        SetSegment(Seg2, Strength, 2);
        SetSegment(Seg3, Strength, 3);
        SetSegment(Seg4, Strength, 4);
    }

    private static void SetSegment(Border border, int strength, int segIndex)
    {
        border.Classes.Remove("seg-inactive");
        border.Classes.Remove("seg-weak");
        border.Classes.Remove("seg-medium");
        border.Classes.Remove("seg-good");
        border.Classes.Remove("seg-strong");

        border.Classes.Add(strength < segIndex ? "seg-inactive" : strength switch
        {
            1 => "seg-weak",
            2 => "seg-medium",
            3 => "seg-good",
            _ => "seg-strong"
        });
    }
}