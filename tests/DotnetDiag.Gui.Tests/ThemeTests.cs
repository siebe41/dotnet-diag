using Avalonia.Media;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.Tests;

/// <summary>Every built-in theme has to stay readable: these are WCAG 2 contrast ratios, not opinions.</summary>
public class ThemeTests
{
    public static TheoryData<string> ThemeIds => new(ThemeCatalog.All.Select(t => t.Id));

    [Fact]
    public void Catalog_offers_light_dark_high_contrast_and_more()
    {
        Assert.Contains(ThemeCatalog.All, t => t is { IsDark: false, IsHighContrast: false });
        Assert.Contains(ThemeCatalog.All, t => t is { IsDark: true, IsHighContrast: false });
        Assert.Contains(ThemeCatalog.All, t => t is { IsDark: true, IsHighContrast: true });
        Assert.Contains(ThemeCatalog.All, t => t is { IsDark: false, IsHighContrast: true });
        Assert.True(ThemeCatalog.All.Count >= 8);

        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Name).Distinct().Count());
        Assert.All(ThemeCatalog.All, t => Assert.Same(t, ThemeCatalog.Find(t.Id.ToUpperInvariant())));
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Text_is_readable_on_every_surface(string id)
    {
        var theme = ThemeCatalog.Find(id)!;
        // Body text: AAA (7:1). Secondary text: AA (4.5:1). High-contrast themes go well beyond both.
        var (body, secondary) = theme.IsHighContrast ? (15.0, 10.0) : (7.0, 4.5);

        foreach (var surface in Surfaces(theme))
        {
            AssertContrast(theme.Text, surface, body, $"{id}: text");
            AssertContrast(theme.TextMuted, surface, secondary, $"{id}: muted text");
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Colored_text_meets_AA_on_every_surface_and_on_its_own_badge(string id)
    {
        var theme = ThemeCatalog.Find(id)!;
        var minimum = theme.IsHighContrast ? 7.0 : 4.5;

        foreach (var tone in Enum.GetValues<Tone>().Where(t => t != Tone.None))
        {
            foreach (var surface in Surfaces(theme).Append(theme.SoftColorOf(tone)))
            {
                AssertContrast(theme.TextColorOf(tone), surface, minimum, $"{id}: {tone} text");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Colored_text_is_the_tone_itself_or_a_close_tint_of_it(string id)
    {
        var theme = ThemeCatalog.Find(id)!;

        foreach (var tone in Enum.GetValues<Tone>().Where(t => t is not (Tone.None or Tone.Neutral)))
        {
            var color = theme.ColorOf(tone);
            var text = theme.TextColorOf(tone);

            // Unchanged where the tone is already readable; otherwise the same hue, only lighter or darker.
            if (Surfaces(theme).Append(theme.SoftColorOf(tone)).All(s => Contrast.Ratio(color, s) >= (theme.IsHighContrast ? 7.0 : 4.5)))
            {
                Assert.Equal(color, text);
            }
            else
            {
                Assert.InRange(Math.Abs(color.ToHsl().H - text.ToHsl().H), 0, 12);
                Assert.True(text.ToHsl().S > 0.3, $"{id}: {tone} text {text} has lost its color");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_accent_is_visible_as_a_fill_and_an_outline(string id)
    {
        var theme = ThemeCatalog.Find(id)!;

        // Buttons, the selection and focus outlines are not text: WCAG asks 3:1 against what is next to them.
        foreach (var surface in Surfaces(theme))
        {
            AssertContrast(theme.Accent, surface, 3.0, $"{id}: accent as a control color");
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Accent_fills_and_their_hover_state_keep_their_label_readable(string id)
    {
        var theme = ThemeCatalog.Find(id)!;
        var colors = ThemeManager.ColorResources(theme);
        var minimum = theme.IsHighContrast ? 7.0 : 4.5;

        AssertContrast(theme.OnAccent, theme.Accent, minimum, $"{id}: primary button");
        AssertContrast(theme.OnAccent, colors["DiagAccentHover"], minimum, $"{id}: hovered primary button");

        // Result badges print the window background color on a solid status fill.
        foreach (var tone in (Tone[])[Tone.Error, Tone.Warning, Tone.Success, Tone.Neutral])
        {
            AssertContrast(theme.Background, theme.ColorOf(tone), minimum, $"{id}: {tone} result badge");
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Borders_and_layers_are_distinguishable(string id)
    {
        var theme = ThemeCatalog.Find(id)!;

        // Non-text boundaries: 3:1 where the border is the only cue (high contrast), visible elsewhere.
        var border = theme.IsHighContrast ? 7.0 : 1.3;
        AssertContrast(theme.Border, theme.Background, border, $"{id}: border on background");
        AssertContrast(theme.Border, theme.Surface, border, $"{id}: border on surface");

        Assert.Equal(theme.IsDark, Contrast.RelativeLuminance(theme.Background) < Contrast.RelativeLuminance(theme.Text));
        Assert.All(ThemeManager.ColorResources(theme).Values, color => Assert.Equal(255, color.A));
    }

    [Fact]
    public void Brand_themes_use_the_exact_brand_blue_and_orange()
    {
        var blue = Color.Parse("#228FA7");
        var orange = Color.Parse("#D08C34");

        foreach (var theme in (AppTheme[])[ThemeCatalog.Dark, ThemeCatalog.Light, ThemeCatalog.Neural])
        {
            Assert.Equal(blue, theme.Accent);
            Assert.Equal(orange, theme.Secondary);

            // The exact colors are what the app paints with: primary buttons, selection, icons, the mark.
            var resources = ThemeManager.ColorResources(theme);
            Assert.Equal(blue, resources["DiagAccent"]);
            Assert.Equal(orange, resources["DiagSecondary"]);
        }

        // Circuit swaps which one leads.
        Assert.Equal(orange, ThemeCatalog.Circuit.Accent);
        Assert.Equal(blue, ThemeCatalog.Circuit.Secondary);

        // System resolves to Dark or Light, so the brand pair is what a new user sees.
        Assert.Equal(blue, ThemeManager.Resolve(ThemeManager.SystemThemeId).Accent);
    }

    [Fact]
    public void Unknown_or_system_theme_ids_fall_back_to_a_real_theme()
    {
        Assert.Same(ThemeCatalog.Nord, ThemeManager.Resolve("NORD"));
        Assert.Same(ThemeCatalog.Dark, ThemeManager.Resolve(ThemeManager.SystemThemeId));
        Assert.Same(ThemeCatalog.Dark, ThemeManager.Resolve("no-such-theme"));
        Assert.Same(ThemeCatalog.Dark, ThemeManager.Resolve(null));
    }

    [Fact]
    public void Contrast_math_matches_the_WCAG_reference_values()
    {
        Assert.Equal(21, Contrast.Ratio(Colors.Black, Colors.White), 2);
        Assert.Equal(1, Contrast.Ratio(Colors.Teal, Colors.Teal), 2);
        Assert.Equal(4.54, Contrast.Ratio(Color.Parse("#767676"), Colors.White), 2); // the classic AA boundary grey
        Assert.Equal(Color.Parse("#808080"), Contrast.Blend(Colors.Black, Colors.White, 0.5));
    }

    private static IEnumerable<Color> Surfaces(AppTheme theme) => [theme.Background, theme.Surface, theme.SurfaceRaised];

    private static void AssertContrast(Color foreground, Color background, double minimum, string what)
    {
        var ratio = Contrast.Ratio(foreground, background);
        Assert.True(ratio >= minimum, $"{what} {foreground} on {background} is {ratio:0.00}:1, needs {minimum:0.0}:1");
    }
}
