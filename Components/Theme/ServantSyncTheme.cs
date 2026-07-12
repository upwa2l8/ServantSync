using MudBlazor;

namespace ServantSync.Components.Theme;

/// <summary>
/// Brand-aligned MudBlazor theme. Single source of truth for the palette +
/// typography that the rest of the app binds to. All values map 1:1 to the
/// color tokens table in <c>BRANDING.md</c>; any swap here is the single
/// point of change for a future rebrand.
///
/// Phase 1 ships light only — dark toggle was deferred as planned. The
/// <c>PaletteDark</c> slot is left at the MudBlazor default for Phase 2.
/// </summary>
public static class ServantSyncTheme
{
    private const string BrandFontStack =
        "'Helvetica Neue', Helvetica, Arial, sans-serif";

    // BRANDING.md → "Color tokens" table. Keep code-in-sync with that table.
    private const string Indigo    = "#3730a3"; // coolSide / 0%   ← Primary
    private const string Violet    = "#7c3aed"; // coolSide / 60%  ← Secondary
    private const string Purple    = "#a855f7"; // coolSide / 100% ← Tertiary
    private const string Amber     = "#f59e0b"; // warmSide / 0%   ← Warning
    private const string Rose      = "#e11d48"; // warmSide / 100% ← Error
    private const string Mint      = "#15803d"; // chosen to pair with cool palette
    private const string Sky       = "#0ea5e9"; // chosen info-blue (calmer than Bootstrap)
    private const string NavbarBg  = "#1f1f2e"; // Navbar backdrop (theme-color too)
    private const string SurfaceBg = "#fafafa"; // page background (one notch off-white)
    private const string CardBg    = "#ffffff";
    private const string Ink       = "#111827"; // body text default
    private const string Muted     = "#6b7280"; // text-secondary
    private const string DrawerBg  = "#ffffff";
    private const string DrawerInk = "#111827";

    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary            = Indigo,
            PrimaryContrastText= "#ffffff",
            Secondary          = Violet,
            SecondaryContrastText = "#ffffff",
            Tertiary           = Purple,
            Success            = Mint,
            Warning            = Amber,
            Error              = Rose,
            Info               = Sky,
            Dark               = NavbarBg,
            Background         = SurfaceBg,
            Surface            = CardBg,
            DrawerBackground   = DrawerBg,
            DrawerText         = DrawerInk,
            AppbarBackground   = NavbarBg,
            TextPrimary        = Ink,
            TextSecondary      = Muted,
            TextDisabled       = "#9ca3af",
            ActionDefault      = Ink,
            ActionDisabled     = "#c4c8cb",
            Divider            = "#e5e7eb",
            LinesDefault       = "#e5e7eb",
            TableLines         = "#e5e7eb",
            TableStriped       = "#f8f9fa",
            TableHover         = "#f3f4f6",
        },
        // Phase-2 placeholder. Leave at MudBlazor defaults for now; wiring up
        // dark mode is a follow-up round (per the user's "phase 2" note).
        PaletteDark = new PaletteDark(),
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { BrandFontStack },
                FontSize   = "0.95rem",
                LineHeight = "1.55",
            },
            H1 = new H1Typography { FontFamily = new[] { BrandFontStack }, FontSize = "2rem",    FontWeight = "700" },
            H2 = new H2Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.6rem",  FontWeight = "700" },
            H3 = new H3Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.35rem", FontWeight = "600" },
            H4 = new H4Typography { FontFamily = new[] { BrandFontStack }, FontSize = "1.15rem", FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = new[] { BrandFontStack }, FontWeight = "600", TextTransform = "none" },
        },
    };
}
