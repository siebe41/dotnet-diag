using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace DotnetDiag.Gui.Theming;

/// <summary>
/// Turns an <see cref="AppTheme"/> into application resources. Views only ever reference the <c>Diag*</c>
/// resource keys with DynamicResource, so switching themes repaints everything without rebuilding a view.
/// </summary>
public static class ThemeManager
{
    /// <summary>Pseudo theme id: follow the operating system's light/dark and contrast settings.</summary>
    public const string SystemThemeId = "system";

    public const double MinTextScale = 0.85;
    public const double MaxTextScale = 1.6;

    /// <summary>Code, logs and labels. The first installed family wins.</summary>
    public static readonly FontFamily MonoFont =
        new("Cascadia Code,Cascadia Mono,Consolas,Menlo,Monaco,DejaVu Sans Mono,Liberation Mono,monospace");

    public static AppTheme Current { get; private set; } = ThemeCatalog.Dark;

    /// <summary>The theme for a stored id; "system" or an unknown id follows the OS (dark when it does not say).</summary>
    public static AppTheme Resolve(string? id, PlatformColorValues? platform = null)
    {
        if (ThemeCatalog.Find(id) is { } theme)
        {
            return theme;
        }

        var dark = platform?.ThemeVariant != PlatformThemeVariant.Light;
        var highContrast = platform?.ContrastPreference == ColorContrastPreference.High;
        return (dark, highContrast) switch
        {
            (true, true) => ThemeCatalog.HighContrast,
            (false, true) => ThemeCatalog.HighContrastLight,
            (true, false) => ThemeCatalog.Dark,
            _ => ThemeCatalog.Light,
        };
    }

    /// <summary>Variant of a tone's brush: the tone itself, its badge background, or its readable text color.</summary>
    public const string Solid = "";
    public const string Soft = "Soft";
    public const string Text = "Text";

    /// <summary>Resource key for a tone's brush, e.g. "DiagAccentBrush", "DiagAccentSoftBrush", "DiagAccentTextBrush".</summary>
    public static string BrushKey(Tone tone, string variant = Solid) => $"Diag{tone}{variant}Brush";

    /// <summary>Every color resource a theme produces, keyed without the "Brush" suffix.</summary>
    public static IReadOnlyDictionary<string, Color> ColorResources(AppTheme theme)
    {
        var colors = new Dictionary<string, Color>
        {
            ["DiagBackground"] = theme.Background,
            ["DiagSurface"] = theme.Surface,
            ["DiagSurfaceRaised"] = theme.SurfaceRaised,
            ["DiagHover"] = Contrast.Blend(theme.SurfaceRaised, theme.Text, 0.08),
            ["DiagBorder"] = theme.Border,
            ["DiagBorderStrong"] = theme.IsHighContrast ? theme.Text : Contrast.Blend(theme.Border, theme.Text, 0.35),
            ["DiagText"] = theme.Text,
            ["DiagTextMuted"] = theme.TextMuted,
            ["DiagOnAccent"] = theme.OnAccent,

            // Moves the accent away from the label drawn on it, so hovering never lowers contrast.
            ["DiagAccentHover"] = Contrast.Blend(
                theme.Accent,
                Contrast.RelativeLuminance(theme.OnAccent) < Contrast.RelativeLuminance(theme.Accent) ? Colors.White : Colors.Black,
                0.18),
        };

        foreach (var tone in Enum.GetValues<Tone>().Where(t => t != Tone.None))
        {
            colors[$"Diag{tone}{Solid}"] = theme.ColorOf(tone);
            colors[$"Diag{tone}{Soft}"] = theme.SoftColorOf(tone);
            colors[$"Diag{tone}{Text}"] = theme.TextColorOf(tone);
        }

        return colors;
    }

    public static void Apply(Application app, AppTheme theme)
    {
        foreach (var (key, color) in ColorResources(theme))
        {
            app.Resources[key + "Brush"] = new ImmutableSolidColorBrush(color);
        }

        app.Resources["DiagBorderThickness"] = new Thickness(theme.IsHighContrast ? 2 : 1);

        // Fluent buttons have no outline by default; give them one that high-contrast themes make prominent.
        var outline = new ImmutableSolidColorBrush(theme.IsHighContrast ? theme.Text : theme.Border);
        foreach (var key in (string[])["ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
                     "ToggleButtonBorderBrush", "ToggleButtonBorderBrushPointerOver", "ToggleButtonBorderBrushPressed"])
        {
            app.Resources[key] = outline;
        }

        var variant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            fluent.Palettes[variant] = FluentPalette(theme);
        }

        app.RequestedThemeVariant = variant;
        Current = theme;
    }

    /// <summary>Text size and density, independent of the color theme.</summary>
    public static void ApplyMetrics(Application app, double textScale, bool compact)
    {
        var scale = Math.Clamp(textScale, MinTextScale, MaxTextScale);
        void Size(string key, double size) => app.Resources[key] = Math.Round(size * scale, 1);

        Size("DiagFontSizeSmall", 12);
        Size("DiagFontSize", 14);
        Size("DiagFontSizeMedium", 16);
        Size("DiagFontSizeLarge", 20);
        Size("DiagFontSizeTitle", 26);
        Size("DiagFontSizeMono", 13);
        Size("ControlContentThemeFontSize", 14); // what the stock Fluent controls use

        app.Resources["DiagMonoFont"] = MonoFont;
        app.Resources["DiagRowPadding"] = compact ? new Thickness(12, 5) : new Thickness(14, 10);
        app.Resources["DiagCardPadding"] = compact ? new Thickness(12, 10) : new Thickness(18, 16);
        app.Resources["DiagSpacing"] = compact ? 10d : 18d;
        app.Resources["ExpanderMinHeight"] = compact ? 32d : 40d; // the stock Fluent header is 48 high
    }

    /// <summary>Maps the theme onto the palette the stock Fluent controls (inputs, lists, scroll bars) draw with.</summary>
    private static ColorPaletteResources FluentPalette(AppTheme theme)
    {
        Color Text(byte alpha) => Contrast.WithAlpha(theme.Text, alpha);
        Color Back(byte alpha) => Contrast.WithAlpha(theme.Background, alpha);
        Color Mix(double amount) => Contrast.Blend(theme.Background, theme.Text, amount);

        return new ColorPaletteResources
        {
            Accent = theme.Accent,
            RegionColor = theme.Background,
            ErrorText = theme.Error,
            BaseHigh = theme.Text,
            BaseMediumHigh = Text(0xCC),
            BaseMedium = Text(0x99),
            BaseMediumLow = Text(0x66),
            BaseLow = Text(0x33),
            AltHigh = theme.Background,
            AltMediumHigh = Back(0xCC),
            AltMedium = Back(0x99),
            AltMediumLow = Back(0x66),
            AltLow = Back(0x33),
            ChromeLow = theme.Surface,
            ChromeMediumLow = theme.SurfaceRaised,
            ChromeMedium = theme.SurfaceRaised,
            ChromeHigh = Contrast.Blend(theme.Border, theme.Text, 0.25),
            ChromeGray = Mix(0.55),
            ChromeDisabledLow = Mix(0.5),
            ChromeDisabledHigh = theme.Border,
            ChromeAltLow = Mix(0.9),
            ChromeWhite = theme.OnAccent, // glyphs on accent fills (check marks, toggle knobs)
            ListLow = Text(0x19),
            ListMedium = Text(0x33),
        };
    }
}
