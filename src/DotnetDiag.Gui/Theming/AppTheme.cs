using Avalonia.Media;
using DotnetDiag.Core.Models;

namespace DotnetDiag.Gui.Theming;

/// <summary>Semantic color a view asks for; each theme decides what it looks like.</summary>
public enum Tone
{
    /// <summary>Not painted: the element keeps whatever its style gives it.</summary>
    None,
    Neutral,
    Accent,
    Secondary,
    Violet,
    Mint,
    Error,
    Warning,
    Success,
}

/// <summary>One selectable color theme. Everything the app paints is derived from these values.</summary>
public sealed record AppTheme
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required bool IsDark { get; init; }

    /// <summary>High-contrast themes also get thicker borders and untinted badge backgrounds.</summary>
    public bool IsHighContrast { get; init; }

    /// <summary>Window background.</summary>
    public required Color Background { get; init; }

    /// <summary>Cards and the navigation rail.</summary>
    public required Color Surface { get; init; }

    /// <summary>Inputs, code blocks and hovered rows.</summary>
    public required Color SurfaceRaised { get; init; }

    public required Color Border { get; init; }

    public required Color Text { get; init; }

    public required Color TextMuted { get; init; }

    /// <summary>Primary accent: selection, links, primary buttons, C# compiler codes.</summary>
    public required Color Accent { get; init; }

    /// <summary>Text and glyphs drawn on top of an <see cref="Accent"/> fill.</summary>
    public required Color OnAccent { get; init; }

    /// <summary>Second brand color: MSBuild codes, suggested commands.</summary>
    public required Color Secondary { get; init; }

    /// <summary>NuGet codes and the AI diagnosis.</summary>
    public required Color Violet { get; init; }

    /// <summary>.NET SDK codes and inline code.</summary>
    public required Color Mint { get; init; }

    public required Color Error { get; init; }

    public required Color Warning { get; init; }

    public required Color Success { get; init; }

    public Color ColorOf(Tone tone) => tone switch
    {
        Tone.Accent => Accent,
        Tone.Secondary => Secondary,
        Tone.Violet => Violet,
        Tone.Mint => Mint,
        Tone.Error => Error,
        Tone.Warning => Warning,
        Tone.Success => Success,
        _ => TextMuted,
    };

    /// <summary>
    /// The tone for small text. It is the tone itself wherever that is readable on every surface it is written
    /// on; otherwise it moves toward the body text color, in small steps, only as far as readability needs.
    /// This lets a theme use exact brand colors for fills and icons without writing unreadable text in them.
    /// </summary>
    public Color TextColorOf(Tone tone)
    {
        var color = ColorOf(tone);
        var minimum = IsHighContrast ? 7.0 : 4.5;
        Color[] surfaces = [Background, Surface, SurfaceRaised, SoftColorOf(tone)];

        for (var step = 0; step < 20; step++)
        {
            var candidate = Contrast.Blend(color, Text, step * 0.05);
            if (surfaces.All(surface => Contrast.Ratio(candidate, surface) >= minimum))
            {
                return candidate;
            }
        }

        return Text;
    }

    /// <summary>Background of a badge in <paramref name="tone"/>: a faint tint, or none in high contrast.</summary>
    public Color SoftColorOf(Tone tone) =>
        IsHighContrast || tone is Tone.None or Tone.Neutral ? SurfaceRaised : Contrast.Blend(Surface, ColorOf(tone), 0.14);
}

/// <summary>Which tone the app uses for each kind of diagnostic.</summary>
public static class Tones
{
    public static Tone For(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.CSharpCompiler or DiagnosticCategory.FSharpCompiler or DiagnosticCategory.VisualBasicCompiler => Tone.Accent,
        DiagnosticCategory.MSBuild => Tone.Secondary,
        DiagnosticCategory.NuGet => Tone.Violet,
        DiagnosticCategory.DotnetSdk => Tone.Mint,
        _ => Tone.Neutral,
    };

    public static Tone For(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Tone.Error,
        DiagnosticSeverity.Warning => Tone.Warning,
        _ => Tone.Neutral,
    };

    public static Tone ForCode(string code) => For(DiagnosticCategories.FromCode(code));
}
