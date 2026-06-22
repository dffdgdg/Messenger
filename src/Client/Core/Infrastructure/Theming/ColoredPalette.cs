namespace Core.Infrastructure.Theming;

/// <summary>
/// Полная цветная тема: все фоны строятся из одного акцента.
/// В отличие от AccentPalette (только 15 ключей),
/// здесь перекрашивается абсолютно всё.
/// </summary>
public sealed record ColoredPalette(
    Color PrimaryBg,
    Color SecondaryBg,
    Color ThirdBg,
    Color FourthBg,

    Color ChatHeaderBg,
    Color ChatComposerBg,
    Color ChatComposerBorder,
    Color ChatMsgBgTop,
    Color ChatMsgBgBottom,

    Color ChatGlowPrimary,
    Color ChatGlowSecondary,

    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentMuted,

    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,

    Color Icon,
    Color BorderDefault,
    Color BorderHover,
    Color ControlBg,
    Color ControlBorder,
    Color AvatarPlaceholder,
    Color HoverOverlay,

    Color MessageOwn,
    Color MessageOther,

    Color NavSelectedBg,
    Color NavSelectedFg,
    Color ChatItemSelectedBg,
    Color ChatItemHoverBg,

    Color CardBg,
    Color CardHover,
    Color ItemBg,
    Color HierarchyLine,

    Color PinnedBannerBg,
    Color OnlineIndicatorBorder,

    Color InfoBg,
    Color InfoBorder,

    Color LoginBgStart,
    Color LoginBgMiddle,
    Color LoginBgEnd,
    Color LoginGlowPrimary,
    Color LoginGlowSecondary,
    Color LoginLogoStart,
    Color LoginLogoEnd,
    Color LoginCardBg,
    Color LoginCardBorder
);