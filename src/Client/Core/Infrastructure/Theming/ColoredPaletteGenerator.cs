namespace Core.Infrastructure.Theming;

public static class ColoredPaletteGenerator
{
    /// <summary>
    /// Строит полностью цветную тему из seed-цвета.
    /// Алгоритм: фиксируем H и S акцента, варьируем L для фонов.
    /// Фоны очень тёмные (L 0.06–0.18), акцент яркий (L 0.45–0.60).
    /// </summary>
    public static ColoredPalette GenerateDark(Color accent)
    {
        var (h, s, l) = AccentPaletteGenerator.ToHsl(accent);

        double bgS = Clamp(s * 0.65, 0.20, 0.55);

        var bg0 = FromHsl(h, bgS, 0.065);
        var bg1 = FromHsl(h, bgS, 0.09);
        var bg2 = FromHsl(h, bgS, 0.115);
        var bg3 = FromHsl(h, bgS, 0.14);

        var headerBg = FromHsl(h, bgS, 0.105);
        var composerBg = FromHsl(h, bgS, 0.10);
        var composerBorder = WithAlpha(Color.FromRgb(255, 255, 255), 0x25);

        var msgAreaTop = FromHsl(h, bgS, 0.075);
        var msgAreaBottom = FromHsl(h, bgS, 0.085);

        var glowPrimary = WithAlpha(accent, 0x20);
        var glowSecondary = WithAlpha(AccentPaletteGenerator.FromHsl(Wrap(h + 0.08), Clamp(s, 0.5, 0.85), Clamp(l, 0.4, 0.65)),
            0x10);

        double aL = Clamp(l, 0.45, 0.62);
        double aS = Clamp(s, 0.55, 0.88);
        var accentColor = AccentPaletteGenerator.FromHsl(h, aS, aL);
        var accentHover = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL + 0.09, 0.50, 0.72));
        var accentPressed = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL - 0.08, 0.34, 0.55));
        var accentMuted = WithAlpha(accentColor, 0x40);

        var textPrimary = FromHsl(h, 0.15, 0.93);
        var textSecondary = FromHsl(h, 0.18, 0.65);
        var textMuted = FromHsl(h, 0.18, 0.42);

        var icon = FromHsl(h, 0.18, 0.58);

        var borderDefault = FromHsl(h, bgS, 0.16);
        var borderHover = FromHsl(h, bgS, 0.22);
        var controlBg = FromHsl(h, bgS, 0.11);
        var controlBorder = FromHsl(h, bgS, 0.16);
        var avatarPh = FromHsl(h, bgS, 0.22);
        var hoverOverlay = WithAlpha(Color.FromRgb(255, 255, 255), 0x18);

        var msgOwn = FromHsl(h, Clamp(aS * 0.65, 0.28, 0.55), Clamp(aL - 0.18, 0.18, 0.36));
        var msgOther = FromHsl(h, bgS, 0.13);

        var navSelBg = FromHsl(h, Clamp(aS * 0.55, 0.22, 0.50), Clamp(aL - 0.16, 0.20, 0.35));
        var navSelFg = accentColor;
        var chatSelBg = FromHsl(h, Clamp(aS * 0.52, 0.20, 0.48), Clamp(aL - 0.14, 0.22, 0.34));
        var chatHover = FromHsl(h, bgS, 0.12);

        var cardBg = FromHsl(h, bgS, 0.115);
        var cardHover = FromHsl(h, bgS, 0.14);
        var itemBg = FromHsl(h, bgS, 0.09);
        var hierarchyLine = FromHsl(h, bgS, 0.20);

        var pinnedBg = FromHsl(h, Clamp(aS * 0.50, 0.20, 0.45), Clamp(aL - 0.15, 0.20, 0.33));
        var onlineBorder = bg0;

        var infoBg = WithAlpha(accentColor, 0x2D);
        var infoBorder = WithAlpha(accentColor, 0x80);

        var loginStart = FromHsl(h, bgS, 0.050);
        var loginMiddle = FromHsl(h, bgS, 0.070);
        var loginEnd = FromHsl(h, bgS, 0.090);
        var loginGlow1 = WithAlpha(accentColor, 0x20);
        var loginGlow2 = WithAlpha(accentHover, 0x15);
        var logoStart = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL + 0.08, 0.50, 0.72));
        var logoEnd = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL - 0.10, 0.30, 0.50));
        var loginCard = WithAlpha(FromHsl(h, bgS, 0.11), 0xF0);
        var loginBorder = WithAlpha(Color.FromRgb(255, 255, 255), 0x30);

        return new ColoredPalette(
            PrimaryBg: bg0,
            SecondaryBg: bg1,
            ThirdBg: bg2,
            FourthBg: bg3,
            ChatHeaderBg: headerBg,
            ChatComposerBg: composerBg,
            ChatComposerBorder: composerBorder,
            ChatMsgBgTop: msgAreaTop,
            ChatMsgBgBottom: msgAreaBottom,
            ChatGlowPrimary: glowPrimary,
            ChatGlowSecondary: glowSecondary,
            Accent: accentColor,
            AccentHover: accentHover,
            AccentPressed: accentPressed,
            AccentMuted: accentMuted,
            TextPrimary: textPrimary,
            TextSecondary: textSecondary,
            TextMuted: textMuted,
            Icon: icon,
            BorderDefault: borderDefault,
            BorderHover: borderHover,
            ControlBg: controlBg,
            ControlBorder: controlBorder,
            AvatarPlaceholder: avatarPh,
            HoverOverlay: hoverOverlay,
            MessageOwn: msgOwn,
            MessageOther: msgOther,
            NavSelectedBg: navSelBg,
            NavSelectedFg: navSelFg,
            ChatItemSelectedBg: chatSelBg,
            ChatItemHoverBg: chatHover,
            CardBg: cardBg,
            CardHover: cardHover,
            ItemBg: itemBg,
            HierarchyLine: hierarchyLine,
            PinnedBannerBg: pinnedBg,
            OnlineIndicatorBorder: onlineBorder,
            InfoBg: infoBg,
            InfoBorder: infoBorder,
            LoginBgStart: loginStart,
            LoginBgMiddle: loginMiddle,
            LoginBgEnd: loginEnd,
            LoginGlowPrimary: loginGlow1,
            LoginGlowSecondary: loginGlow2,
            LoginLogoStart: logoStart,
            LoginLogoEnd: logoEnd,
            LoginCardBg: loginCard,
            LoginCardBorder: loginBorder
        );
    }
    private static ColoredPalette GenerateLight(Color accent)
    {
        var (h, s, l) = AccentPaletteGenerator.ToHsl(accent);

        double bgS = Clamp(s * 0.25, 0.08, 0.30);

        var bg0 = FromHsl(h, bgS, 0.96);
        var bg1 = FromHsl(h, bgS, 0.99);
        var bg2 = FromHsl(h, bgS, 0.94);
        var bg3 = FromHsl(h, bgS, 0.91);

        var headerBg = FromHsl(h, bgS, 0.97);
        var composerBg = FromHsl(h, bgS, 0.99);
        var composerBorder = WithAlpha(Color.FromRgb(0, 0, 0), 0x18);

        var msgAreaTop = FromHsl(h, bgS, 0.95);
        var msgAreaBottom = FromHsl(h, bgS, 0.93);

        double aL = Clamp(l, 0.38, 0.55);
        double aS = Clamp(s, 0.55, 0.85);
        var accentColor = AccentPaletteGenerator.FromHsl(h, aS, aL);
        var accentHover = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL - 0.07, 0.28, 0.48));
        var accentPressed = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL - 0.13, 0.22, 0.42));
        var accentMuted = WithAlpha(accentColor, 0x22);

        var glowPrimary = WithAlpha(accentColor, 0x08);
        var glowSecondary = WithAlpha(AccentPaletteGenerator.FromHsl(Wrap(h + 0.08), aS, aL), 0x05);

        var textPrimary = Color.FromRgb(17, 17, 17);
        var textSecondary = Color.FromRgb(90, 95, 110);
        var textMuted = Color.FromRgb(150, 155, 168);
        var icon = FromHsl(h, 0.12, 0.52);

        var borderDefault = FromHsl(h, bgS, 0.88);
        var borderHover = FromHsl(h, bgS, 0.82);
        var controlBg = FromHsl(h, bgS, 0.99);
        var controlBorder = FromHsl(h, bgS, 0.88);
        var avatarPh = FromHsl(h, bgS, 0.82);
        var hoverOverlay = WithAlpha(Color.FromRgb(0, 0, 0), 0x08);

        var msgOwn = FromHsl(h, Clamp(aS * 0.30, 0.12, 0.40), Clamp(aL + 0.36, 0.80, 0.94));
        var msgOther = FromHsl(h, bgS, 0.99);

        var navSelBg = FromHsl(h, Clamp(aS * 0.28, 0.10, 0.38), Clamp(aL + 0.38, 0.83, 0.95));
        var navSelFg = accentColor;
        var chatSelBg = FromHsl(h, Clamp(aS * 0.25, 0.08, 0.35), Clamp(aL + 0.36, 0.82, 0.94));
        var chatHover = FromHsl(h, bgS, 0.93);

        var cardBg = FromHsl(h, bgS, 0.99);
        var cardHover = FromHsl(h, bgS, 0.95);
        var itemBg = FromHsl(h, bgS, 0.97);
        var hierarchyLine = FromHsl(h, bgS, 0.85);

        var pinnedBg = FromHsl(h, Clamp(aS * 0.25, 0.10, 0.35), Clamp(aL + 0.38, 0.84, 0.95));
        var onlineBorder = bg0;

        var infoBg = WithAlpha(accentColor, 0x15);
        var infoBorder = WithAlpha(accentColor, 0x55);

        var loginStart = FromHsl(h, bgS, 0.94);
        var loginMiddle = FromHsl(h, bgS, 0.96);
        var loginEnd = FromHsl(h, bgS, 0.99);
        var loginGlow1 = WithAlpha(accentColor, 0x10);
        var loginGlow2 = WithAlpha(accentHover, 0x08);
        var logoStart = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL + 0.08, 0.48, 0.68));
        var logoEnd = AccentPaletteGenerator.FromHsl(h, aS, Clamp(aL - 0.10, 0.28, 0.46));
        var loginCard = WithAlpha(Color.FromRgb(255, 255, 255), 0xF5);
        var loginBorder = WithAlpha(Color.FromRgb(0, 0, 0), 0x18);

        return new ColoredPalette(
            PrimaryBg: bg0,
            SecondaryBg: bg1,
            ThirdBg: bg2,
            FourthBg: bg3,
            ChatHeaderBg: headerBg,
            ChatComposerBg: composerBg,
            ChatComposerBorder: composerBorder,
            ChatMsgBgTop: msgAreaTop,
            ChatMsgBgBottom: msgAreaBottom,
            ChatGlowPrimary: glowPrimary,
            ChatGlowSecondary: glowSecondary,
            Accent: accentColor,
            AccentHover: accentHover,
            AccentPressed: accentPressed,
            AccentMuted: accentMuted,
            TextPrimary: textPrimary,
            TextSecondary: textSecondary,
            TextMuted: textMuted,
            Icon: icon,
            BorderDefault: borderDefault,
            BorderHover: borderHover,
            ControlBg: controlBg,
            ControlBorder: controlBorder,
            AvatarPlaceholder: avatarPh,
            HoverOverlay: hoverOverlay,
            MessageOwn: msgOwn,
            MessageOther: msgOther,
            NavSelectedBg: navSelBg,
            NavSelectedFg: navSelFg,
            ChatItemSelectedBg: chatSelBg,
            ChatItemHoverBg: chatHover,
            CardBg: cardBg,
            CardHover: cardHover,
            ItemBg: itemBg,
            HierarchyLine: hierarchyLine,
            PinnedBannerBg: pinnedBg,
            OnlineIndicatorBorder: onlineBorder,
            InfoBg: infoBg,
            InfoBorder: infoBorder,
            LoginBgStart: loginStart,
            LoginBgMiddle: loginMiddle,
            LoginBgEnd: loginEnd,
            LoginGlowPrimary: loginGlow1,
            LoginGlowSecondary: loginGlow2,
            LoginLogoStart: logoStart,
            LoginLogoEnd: logoEnd,
            LoginCardBg: loginCard,
            LoginCardBorder: loginBorder
        );
    }
    public static bool ShouldUseDarkBase(Color accent)
    {
        var (_, _, l) = AccentPaletteGenerator.ToHsl(accent);
        return l <= 0.55;
    }

    public static ColoredPalette Generate(Color accent)
        => ShouldUseDarkBase(accent) ? GenerateDark(accent) : GenerateLight(accent);
    
    private static Color FromHsl(double h, double s, double l)
        => AccentPaletteGenerator.FromHsl(h, s, l);

    private static Color WithAlpha(Color c, byte alpha)
        => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static double Clamp(double v, double min, double max)
        => v < min ? min : v > max ? max : v;

    private static double Wrap(double h)
    {
        while (h < 0) h += 1;
        while (h >= 1) h -= 1;
        return h;
    }
}