namespace Core.Infrastructure.Theming;

/// <summary>
/// Все цвета, которые зависят от выбранного акцента.
/// Фоны и текст остаются из базовой темы (Dark/Light).
/// </summary>
public sealed record AccentPalette(
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentMuted,

    Color MessageOwn,

    Color NavSelectedBg,
    Color NavSelectedFg,
    Color ChatItemSelectedBg,

    Color PinnedBannerBg,

    Color InfoBg,
    Color InfoBorder,

    Color LoginLogoStart,
    Color LoginLogoEnd,

    Color ChatGlowPrimary,
    Color ChatGlowSecondary
);