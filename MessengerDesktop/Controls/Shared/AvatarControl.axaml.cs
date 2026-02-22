using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Controls;

public partial class AvatarControl : UserControl
{
    #region Styled Properties

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<AvatarControl, double>(nameof(Size), 40);

    public new static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<AvatarControl, double>(nameof(FontSize), 14);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<AvatarControl, double>(nameof(IconSize), 18);

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<AvatarControl, string?>(nameof(Source));

    public static readonly StyledProperty<string?> DisplayNameProperty =
        AvaloniaProperty.Register<AvatarControl, string?>(nameof(DisplayName));

    public static readonly StyledProperty<bool> IsOnlineProperty =
        AvaloniaProperty.Register<AvatarControl, bool>(nameof(IsOnline));

    public static readonly StyledProperty<bool> ShowOnlineIndicatorProperty =
        AvaloniaProperty.Register<AvatarControl, bool>(nameof(ShowOnlineIndicator), true);

    public static readonly StyledProperty<Geometry?> FallbackIconProperty =
        AvaloniaProperty.Register<AvatarControl, Geometry?>(nameof(FallbackIcon));

    public static readonly StyledProperty<IBrush?> PlaceholderBackgroundProperty =
        AvaloniaProperty.Register<AvatarControl, IBrush?>(nameof(PlaceholderBackground));

    public static readonly StyledProperty<IBrush?> PlaceholderForegroundProperty =
        AvaloniaProperty.Register<AvatarControl, IBrush?>(nameof(PlaceholderForeground));

    public static readonly StyledProperty<bool> IsCircularProperty =
        AvaloniaProperty.Register<AvatarControl, bool>(nameof(IsCircular), true);

    #endregion

    #region Direct Properties

    private CornerRadius _onlineIndicatorCornerRadius = new(5);
    public static readonly DirectProperty<AvatarControl, CornerRadius>
        OnlineIndicatorCornerRadiusProperty =
            AvaloniaProperty.RegisterDirect<AvatarControl, CornerRadius>(
                nameof(OnlineIndicatorCornerRadius), o => o.OnlineIndicatorCornerRadius);

    private string? _imageSource;
    public static readonly DirectProperty<AvatarControl, string?>
        ImageSourceProperty = AvaloniaProperty.RegisterDirect
        <AvatarControl, string?>(nameof(ImageSource), o => o.ImageSource);

    private bool _hasImage;
    public static readonly DirectProperty<AvatarControl, bool>
        HasImageProperty = AvaloniaProperty.RegisterDirect
        <AvatarControl, bool>(nameof(HasImage), o => o.HasImage);

    private bool _showInitials;
    public static readonly DirectProperty<AvatarControl, bool>
        ShowInitialsProperty = AvaloniaProperty.RegisterDirect
        <AvatarControl, bool>(nameof(ShowInitials), o => o.ShowInitials);

    private bool _showIcon;
    public static readonly DirectProperty<AvatarControl, bool>
        ShowIconProperty = AvaloniaProperty.RegisterDirect<AvatarControl, bool>
        (nameof(ShowIcon), o => o.ShowIcon);

    private string _initials = "?";
    public static readonly DirectProperty<AvatarControl, string>
        InitialsProperty = AvaloniaProperty.RegisterDirect<AvatarControl, string>
        (nameof(Initials), o => o.Initials);

    private Geometry? _iconData;
    public static readonly DirectProperty<AvatarControl, Geometry?>
        IconDataProperty = AvaloniaProperty.RegisterDirect<AvatarControl, Geometry?>
        (nameof(IconData), o => o.IconData);

    private double _onlineIndicatorSize = 10;
    public static readonly DirectProperty<AvatarControl, double>
        OnlineIndicatorSizeProperty = AvaloniaProperty.RegisterDirect<AvatarControl, double>
        (nameof(OnlineIndicatorSize), o => o.OnlineIndicatorSize);

    private bool _showOnlineStatus;
    public static readonly DirectProperty<AvatarControl, bool>
        ShowOnlineStatusProperty = AvaloniaProperty.RegisterDirect<AvatarControl, bool>
        (nameof(ShowOnlineStatus), o => o.ShowOnlineStatus);

    #endregion

    #region Property Accessors

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public new double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public bool IsOnline
    {
        get => GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public bool ShowOnlineIndicator
    {
        get => GetValue(ShowOnlineIndicatorProperty);
        set => SetValue(ShowOnlineIndicatorProperty, value);
    }

    public Geometry? FallbackIcon
    {
        get => GetValue(FallbackIconProperty);
        set => SetValue(FallbackIconProperty, value);
    }

    public IBrush? PlaceholderBackground
    {
        get => GetValue(PlaceholderBackgroundProperty);
        set => SetValue(PlaceholderBackgroundProperty, value);
    }

    public IBrush? PlaceholderForeground
    {
        get => GetValue(PlaceholderForegroundProperty);
        set => SetValue(PlaceholderForegroundProperty, value);
    }

    public bool IsCircular
    {
        get => GetValue(IsCircularProperty);
        set => SetValue(IsCircularProperty, value);
    }

    public CornerRadius OnlineIndicatorCornerRadius
    {
        get => _onlineIndicatorCornerRadius;
        private set => SetAndRaise(OnlineIndicatorCornerRadiusProperty, ref _onlineIndicatorCornerRadius, value);
    }

    public string? ImageSource
    {
        get => _imageSource;
        private set => SetAndRaise(ImageSourceProperty, ref _imageSource, value);
    }

    public bool HasImage
    {
        get => _hasImage;
        private set => SetAndRaise(HasImageProperty, ref _hasImage, value);
    }

    public bool ShowInitials
    {
        get => _showInitials;
        private set => SetAndRaise(ShowInitialsProperty, ref _showInitials, value);
    }

    public bool ShowIcon
    {
        get => _showIcon;
        private set => SetAndRaise(ShowIconProperty, ref _showIcon, value);
    }

    public string Initials
    {
        get => _initials;
        private set => SetAndRaise(InitialsProperty, ref _initials, value);
    }

    public Geometry? IconData
    {
        get => _iconData;
        private set => SetAndRaise(IconDataProperty, ref _iconData, value);
    }

    public double OnlineIndicatorSize
    {
        get => _onlineIndicatorSize;
        private set => SetAndRaise(OnlineIndicatorSizeProperty, ref _onlineIndicatorSize, value);
    }

    public bool ShowOnlineStatus
    {
        get => _showOnlineStatus;
        private set => SetAndRaise(ShowOnlineStatusProperty, ref _showOnlineStatus, value);
    }

    #endregion

    /// <summary>
    /// Known image file extensions that Avalonia's Bitmap decoder
    /// can handle. Anything else (.pptx, .pdf, .docx) must NOT
    /// be passed to AsyncImageLoader.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".ico", ".avif"
    };

    public AvatarControl()
    {
        InitializeComponent();
        UpdateComputedProperties();
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        PlaceholderBackground ??=
            this.FindResource("AccentLight") as IBrush
            ?? new SolidColorBrush(Color.Parse("#5B5FC7"));

        PlaceholderForeground ??=
            this.FindResource("AccentForeground") as IBrush
            ?? Brushes.White;

        UpdateComputedProperties();
        UpdateCornerRadius();
    }

    static AvatarControl()
    {
        SourceProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.UpdateComputedProperties());
        DisplayNameProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.UpdateComputedProperties());
        FallbackIconProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.UpdateComputedProperties());
        IsOnlineProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.UpdateOnlineStatus());
        ShowOnlineIndicatorProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.UpdateOnlineStatus());
        SizeProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.OnSizeChanged());
        IsCircularProperty.Changed.AddClassHandler<AvatarControl>((x, _) => x.UpdateCornerRadius());
    }

    private void UpdateComputedProperties()
    {
        var source = Source;
        var isValidImage = IsValidImageSource(source);

        // CRITICAL: Only set ImageSource when Source is a valid image URL.
        //
        // Problem this solves:
        //   When Source is null, empty, or a non-image URL (.pptx, .pdf),
        //   the old code would still set ImageSource, causing
        //   AsyncImageLoader to:
        //     1. Make an HTTP request with no/bad URL → error in logs
        //     2. Try to decode a non-image file as Bitmap → exception
        //
        // Now: HasImage=false → Panel with Image is collapsed (IsVisible=false)
        //      → AsyncImageLoader.Source binding never triggers
        HasImage = isValidImage;
        ImageSource = isValidImage ? ToAbsoluteUrl(source!) : null;

        Initials = ExtractInitials(DisplayName);
        IconData = FallbackIcon ?? GetDefaultIcon();

        var hasFallbackIcon = FallbackIcon != null;
        ShowInitials = !HasImage && !string.IsNullOrEmpty(DisplayName) && !hasFallbackIcon;
        ShowIcon = !HasImage && (string.IsNullOrEmpty(DisplayName) || hasFallbackIcon);
    }

    /// <summary>
    /// Validates that the source string is non-empty and points
    /// to a file with a known image extension.
    /// </summary>
    private static bool IsValidImageSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        // Resource URIs are always valid
        if (source.StartsWith("avares://", StringComparison.Ordinal))
            return true;

        // Strip query string for extension check
        // "avatar.webp?v=123456" → "avatar.webp"
        var path = source;
        var queryIndex = source.IndexOf('?');
        if (queryIndex >= 0)
            path = source[..queryIndex];

        var dotIndex = path.LastIndexOf('.');
        if (dotIndex < 0)
            return false; // No extension — can't determine type

        var extension = path[dotIndex..];
        return ImageExtensions.Contains(extension);
    }

    private static string? ToAbsoluteUrl(string url)
    {
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.StartsWith("avares://", StringComparison.Ordinal))
            return url;

        return $"{App.ApiUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }

    private void UpdateOnlineStatus() => ShowOnlineStatus = IsOnline && ShowOnlineIndicator;

    private void OnSizeChanged()
    {
        OnlineIndicatorSize = Size switch
        {
            <= 32 => 8,
            <= 48 => 10,
            <= 64 => 12,
            <= 80 => 14,
            _ => 16
        };

        OnlineIndicatorCornerRadius = new CornerRadius(OnlineIndicatorSize / 2);

        UpdateCornerRadius();
    }

    private void UpdateCornerRadius()
    {
        if (this.FindControl<Border>("AvatarBorder") is { } border)
        {
            border.CornerRadius = IsCircular ? new CornerRadius(Size / 2) : new CornerRadius(8);
        }
    }

    private static string ExtractInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            >= 2 => $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}",

            1 when parts[0].Length >= 2 => $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[0][1])}",

            1 => char.ToUpper(parts[0][0]).ToString(),

            _ => "?"
        };
    }

    private Geometry? GetDefaultIcon() => this.FindResource("PersonIcon") as Geometry;
}