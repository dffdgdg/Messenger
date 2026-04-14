namespace MessengerDesktop.Views.Controls.Shared;

public partial class PasswordStrengthControl : UserControl
{
    public static readonly StyledProperty<int> StrengthProperty =
        AvaloniaProperty.Register<PasswordStrengthControl, int>(nameof(Strength));

    public static readonly StyledProperty<string> StrengthLabelProperty =
        AvaloniaProperty.Register<PasswordStrengthControl, string>(
            nameof(StrengthLabel), string.Empty);

    public static readonly StyledProperty<bool> PasswordsMatchProperty =
        AvaloniaProperty.Register<PasswordStrengthControl, bool>(nameof(PasswordsMatch));

    public static readonly StyledProperty<bool> ShowMatchProperty =
        AvaloniaProperty.Register<PasswordStrengthControl, bool>(nameof(ShowMatch));

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

    public PasswordStrengthControl()
    {
        InitializeComponent();

        StrengthProperty.Changed.AddClassHandler<PasswordStrengthControl>(
            (c, _) => c.UpdateSegmentClasses());

        Loaded += (_, _) => UpdateSegmentClasses();
    }

    private void UpdateSegmentClasses()
    {
        if (Seg1 is null) return;

        var strength = Strength;
        SetSegment(Seg1, strength, 1);
        SetSegment(Seg2, strength, 2);
        SetSegment(Seg3, strength, 3);
        SetSegment(Seg4, strength, 4);
    }

    private static void SetSegment(Border border, int strength, int segIndex)
    {
        border.Classes.Remove("seg-inactive");
        border.Classes.Remove("seg-weak");
        border.Classes.Remove("seg-medium");
        border.Classes.Remove("seg-good");
        border.Classes.Remove("seg-strong");

        if (strength < segIndex)
        {
            border.Classes.Add("seg-inactive");
            return;
        }

        border.Classes.Add(strength switch
        {
            1 => "seg-weak",
            2 => "seg-medium",
            3 => "seg-good",
            _ => "seg-strong"
        });
    }
}