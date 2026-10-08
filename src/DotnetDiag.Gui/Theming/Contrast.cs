using Avalonia.Media;

namespace DotnetDiag.Gui.Theming;

/// <summary>WCAG 2 contrast math, used to derive tints and to verify every theme in the tests.</summary>
public static class Contrast
{
    /// <summary>Contrast ratio between two opaque colors, from 1 (identical) to 21 (black on white).</summary>
    public static double Ratio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>Mixes <paramref name="amount"/> (0–1) of <paramref name="top"/> into <paramref name="bottom"/>.</summary>
    public static Color Blend(Color bottom, Color top, double amount)
    {
        byte Mix(byte from, byte to) => (byte)Math.Round(from + ((to - from) * Math.Clamp(amount, 0, 1)));
        return Color.FromRgb(Mix(bottom.R, top.R), Mix(bottom.G, top.G), Mix(bottom.B, top.B));
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
