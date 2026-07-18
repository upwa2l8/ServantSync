using MudBlazor;

namespace ServantSync.Components.Theme;

/// <summary>
/// "Cyber Chapel" — dark-first, bold, edgy theme for ServantSync.
/// Designed for 15" laptop and Pixel 9 (412px) alike.
/// Deep blue-black backgrounds with neon purple/cyan/pink accents,
/// glassmorphism-ready card surfaces, and Inter typography.
/// </summary>
public static class ServantSyncTheme
{
    private const string BrandFontStack =
        "'Inter', 'Inter Variable', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";

    // ── Dark palette: "Cyber Chapel" ──────────────────────────────
    private const string Deepest   = "#070712"; // page bg — near-black with blue undertone
    private const string Deep      = "#0d0d1f"; // drawer / appbar
    private const string Surface   = "#13132b"; // card surfaces — opaque for dropdown safety
    private const string Primary   = "#a78bfa"; // neon violet — main accent
    private const string Secondary = "#22d3ee"; // cyan — secondary accent
    private const string Tertiary  = "#f472b6"; // hot pink — tertiary
    private const string Success   = "#34d399"; // emerald
    private const string Warning   = "#fbbf24"; // amber
    private const string Error     = "#f87171"; // soft red
    private const string Info      = "#38bdf8"; // sky
    private const string Ink       = "#e2e8f0"; // body text
    private const string Muted     = "#94a3b8"; // secondary text
    private const string Disabled  = "#64748b"; // disabled text

    /// <summary>
    /// Both palettes are the dark scheme so static-SSR pages (Account)
    /// don't flash a white background before the interactive circuit
    /// connects and sets IsDarkMode. See MudProviders.razor.
    /// </summary>
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Black              = "#000000",
            White              = "#ffffff",
            Primary            = Primary,
            PrimaryContrastText= "#0a0a18",
            Secondary          = Secondary,
            SecondaryContrastText = "#0a0a18",
            Tertiary           = Tertiary,
            TertiaryContrastText = "#0a0a18",
            Success            = Success,
            SuccessContrastText= "#0a0a18",
            Warning            = Warning,
            WarningContrastText= "#0a0a18",
            Error              = Error,
            ErrorContrastText  = "#0a0a18",
            Info               = Info,
            InfoContrastText   = "#0a0a18",
            Dark               = Deep,
            DarkContrastText   = Ink,
            Background         = Deepest,
            Surface            = Surface,
            DrawerBackground   = Deep,
            DrawerText         = Ink,
            DrawerIcon         = Muted,
            AppbarBackground   = Deep,
            AppbarText         = Ink,
            TextPrimary        = Ink,
            TextSecondary      = Muted,
            TextDisabled       = Disabled,
            ActionDefault      = Ink,
            ActionDisabled     = "#475569",
            ActionDisabledBackground = "rgba(255,255,255,0.04)",
            Divider            = "rgba(255,255,255,0.08)",
            LinesDefault       = "rgba(255,255,255,0.10)",
            LinesInputs        = "rgba(255,255,255,0.14)",
            TableLines         = "rgba(255,255,255,0.08)",
            TableStriped       = "rgba(255,255,255,0.03)",
            TableHover         = "rgba(255,255,255,0.06)",
            OverlayDark        = "rgba(0,0,0,0.6)",
            OverlayLight       = "rgba(255,255,255,0.04)",
        },
        // Phase-2 dark-mode duplicate — identical so the FOUC
        // (flash of unstyled content) on static-SSR Account pages
        // lands on the same dark scheme.
        PaletteDark = new PaletteDark
        {
            Black              = "#000000",
            White              = "#ffffff",
            Primary            = Primary,
            PrimaryContrastText= "#0a0a18",
            Secondary          = Secondary,
            SecondaryContrastText = "#0a0a18",
            Tertiary           = Tertiary,
            TertiaryContrastText = "#0a0a18",
            Success            = Success,
            SuccessContrastText= "#0a0a18",
            Warning            = Warning,
            WarningContrastText= "#0a0a18",
            Error              = Error,
            ErrorContrastText  = "#0a0a18",
            Info               = Info,
            InfoContrastText   = "#0a0a18",
            Dark               = Deep,
            DarkContrastText   = Ink,
            Background         = Deepest,
            Surface            = Surface,
            DrawerBackground   = Deep,
            DrawerText         = Ink,
            DrawerIcon         = Muted,
            AppbarBackground   = Deep,
            AppbarText         = Ink,
            TextPrimary        = Ink,
            TextSecondary      = Muted,
            TextDisabled       = Disabled,
            ActionDefault      = Ink,
            ActionDisabled     = "#475569",
            ActionDisabledBackground = "rgba(255,255,255,0.04)",
            Divider            = "rgba(255,255,255,0.08)",
            LinesDefault       = "rgba(255,255,255,0.10)",
            LinesInputs        = "rgba(255,255,255,0.14)",
            TableLines         = "rgba(255,255,255,0.08)",
            TableStriped       = "rgba(255,255,255,0.03)",
            TableHover         = "rgba(255,255,255,0.06)",
            OverlayDark        = "rgba(0,0,0,0.6)",
            OverlayLight       = "rgba(255,255,255,0.04)",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft     = "260px",
            DrawerMiniWidthLeft = "64px",
            AppbarHeight        = "56px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily   = new[] { BrandFontStack },
                FontSize     = "0.9375rem",
                LineHeight   = "1.6",
                LetterSpacing = "0.01em",
            },
            H1 = new H1Typography { FontFamily = new[] { BrandFontStack }, FontSize = "2.25rem", FontWeight = "800", LineHeight = "1.2", LetterSpacing = "-0.02em" },
            H2 = new H2Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.75rem", FontWeight = "700", LineHeight = "1.25", LetterSpacing = "-0.015em" },
            H3 = new H3Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.4rem",  FontWeight = "700", LineHeight = "1.3",  LetterSpacing = "-0.01em" },
            H4 = new H4Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.15rem", FontWeight = "600", LineHeight = "1.35" },
            H5 = new H5Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.05rem", FontWeight = "600", LineHeight = "1.4" },
            H6 = new H6Typography { FontFamily = new[] { BrandFontStack }, FontSize = "0.95rem",  FontWeight = "600", LineHeight = "1.45" },
            Subtitle1 = new Subtitle1Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1rem", FontWeight = "500", LineHeight = "1.5" },
            Subtitle2 = new Subtitle2Typography { FontFamily = new[] { BrandFontStack }, FontSize = "0.875rem", FontWeight = "500", LineHeight = "1.5" },
            Body1 = new Body1Typography { FontFamily = new[] { BrandFontStack }, FontSize = "0.9375rem", LineHeight = "1.6" },
            Body2 = new Body2Typography { FontFamily = new[] { BrandFontStack }, FontSize = "0.8125rem", LineHeight = "1.55" },
            Caption = new CaptionTypography { FontFamily = new[] { BrandFontStack }, FontSize = "0.75rem", LineHeight = "1.4" },
            Overline = new OverlineTypography { FontFamily = new[] { BrandFontStack }, FontSize = "0.6875rem", LineHeight = "1.4", LetterSpacing = "0.08em", TextTransform = "uppercase" },
            Button = new ButtonTypography { FontFamily = new[] { BrandFontStack }, FontWeight = "600", TextTransform = "none", LetterSpacing = "0.02em" },
        },
        Shadows = new Shadow
        {
            Elevation = new[]
            {
                // Level 0..25 — tinted with primary purple for a neon glow
                "none",
                "0 1px 3px rgba(167,139,250,0.08), 0 1px 2px rgba(0,0,0,0.3)",
                "0 3px 6px rgba(167,139,250,0.06), 0 2px 4px rgba(0,0,0,0.35)",
                "0 4px 12px rgba(167,139,250,0.07), 0 2px 6px rgba(0,0,0,0.4)",
                "0 6px 16px rgba(167,139,250,0.08), 0 3px 8px rgba(0,0,0,0.45)",
                "0 8px 24px rgba(167,139,250,0.10), 0 4px 12px rgba(0,0,0,0.5)",
                "0 10px 32px rgba(167,139,250,0.10), 0 5px 16px rgba(0,0,0,0.55)",
                "0 12px 40px rgba(167,139,250,0.11), 0 6px 20px rgba(0,0,0,0.6)",
                "0 14px 48px rgba(167,139,250,0.12), 0 7px 24px rgba(0,0,0,0.6)",
                "0 16px 56px rgba(167,139,250,0.12), 0 8px 28px rgba(0,0,0,0.6)",
                "0 18px 64px rgba(167,139,250,0.13), 0 10px 32px rgba(0,0,0,0.6)",
                "0 20px 72px rgba(167,139,250,0.13), 0 12px 36px rgba(0,0,0,0.6)",
                "0 22px 80px rgba(167,139,250,0.14), 0 14px 40px rgba(0,0,0,0.6)",
                "0 24px 88px rgba(167,139,250,0.14), 0 16px 44px rgba(0,0,0,0.6)",
                "0 26px 96px rgba(167,139,250,0.15), 0 18px 48px rgba(0,0,0,0.6)",
                "0 28px 104px rgba(167,139,250,0.15), 0 20px 52px rgba(0,0,0,0.6)",
                "0 30px 112px rgba(167,139,250,0.16), 0 22px 56px rgba(0,0,0,0.6)",
                "0 32px 120px rgba(167,139,250,0.16), 0 24px 60px rgba(0,0,0,0.6)",
                "0 34px 128px rgba(167,139,250,0.17), 0 26px 64px rgba(0,0,0,0.6)",
                "0 36px 136px rgba(167,139,250,0.17), 0 28px 68px rgba(0,0,0,0.6)",
                "0 38px 144px rgba(167,139,250,0.18), 0 30px 72px rgba(0,0,0,0.6)",
                "0 40px 152px rgba(167,139,250,0.18), 0 32px 76px rgba(0,0,0,0.6)",
                "0 42px 160px rgba(167,139,250,0.19), 0 34px 80px rgba(0,0,0,0.6)",
                "0 44px 168px rgba(167,139,250,0.19), 0 36px 84px rgba(0,0,0,0.6)",
                "0 46px 176px rgba(167,139,250,0.20), 0 38px 88px rgba(0,0,0,0.6)",
                "0 48px 184px rgba(167,139,250,0.20), 0 40px 92px rgba(0,0,0,0.6)",
            },
        },
    };
}
