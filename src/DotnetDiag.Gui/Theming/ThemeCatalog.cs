using Avalonia.Media;

namespace DotnetDiag.Gui.Theming;

/// <summary>
/// The built-in themes. Light, Dark, Neural and Circuit use the two Siebert &amp; The Machine brand colors exactly:
/// blue <c>#228FA7</c> and orange <c>#D08C34</c>, on the charcoal of the mark. Circuit swaps which one leads.
/// The remaining themes have palettes of their own. Contrast for every entry is enforced by ThemeTests.
/// </summary>
public static class ThemeCatalog
{
    public const string BrandBlue = "#228FA7";
    public const string BrandOrange = "#D08C34";

    public static readonly AppTheme Dark = Theme(
        "dark", "Dark", "Charcoal with the brand blue and orange", dark: true,
        background: "#141418", surface: "#1B1B20", raised: "#25252B", border: "#3A3A44",
        text: "#E4E4E0", muted: "#A9A9A3",
        accent: BrandBlue, onAccent: "#081417", secondary: BrandOrange,
        violet: "#B9A0F4", mint: "#5FD0B5", error: "#F58082", warning: "#E8C24A", success: "#7FC883");

    public static readonly AppTheme Light = Theme(
        "light", "Light", "Warm paper with the brand blue and orange", dark: false,
        background: "#F5F5F1", surface: "#FFFFFF", raised: "#ECECE6", border: "#CDCDC4",
        text: "#141418", muted: "#55555B",
        accent: BrandBlue, onAccent: "#061317", secondary: BrandOrange,
        violet: "#6340B8", mint: "#0D6E66", error: "#AE231C", warning: "#735600", success: "#1B7331");

    public static readonly AppTheme HighContrast = Theme(
        "high-contrast", "High Contrast", "White on black with heavy borders", dark: true, highContrast: true,
        background: "#000000", surface: "#000000", raised: "#141414", border: "#FFFFFF",
        text: "#FFFFFF", muted: "#E6E6E6",
        accent: "#5CE1FF", onAccent: "#000000", secondary: "#FFB52E",
        violet: "#D9C4FF", mint: "#7DFFD8", error: "#FF9A9A", warning: "#FFE600", success: "#7BFF98");

    public static readonly AppTheme HighContrastLight = Theme(
        "high-contrast-light", "High Contrast Light", "Black on white with heavy borders", dark: false, highContrast: true,
        background: "#FFFFFF", surface: "#FFFFFF", raised: "#F0F0F0", border: "#000000",
        text: "#000000", muted: "#1F1F1F",
        accent: "#00455E", onAccent: "#FFFFFF", secondary: "#5E3300",
        violet: "#3F1F8A", mint: "#004D45", error: "#850000", warning: "#4F3A00", success: "#00501A");

    public static readonly AppTheme Neural = Theme(
        "neural", "Neural", "Deep blue-green, from the network half of the mark", dark: true,
        background: "#0B191F", surface: "#10232B", raised: "#17303A", border: "#2B4C5A",
        text: "#DDECF0", muted: "#97B6C0",
        accent: BrandBlue, onAccent: "#05161B", secondary: BrandOrange,
        violet: "#BBADF7", mint: "#70E1C9", error: "#FF9086", warning: "#EDCF70", success: "#84D79F");

    public static readonly AppTheme Circuit = Theme(
        "circuit", "Circuit", "Warm black with orange traces: the brand pair, orange leading", dark: true,
        background: "#16120D", surface: "#1E1912", raised: "#2A2319", border: "#473B2C",
        text: "#EEE5D7", muted: "#B5A791",
        accent: BrandOrange, onAccent: "#1A1206", secondary: BrandBlue,
        violet: "#C7ABF0", mint: "#70D0B9", error: "#F78A7E", warning: "#EBD264", success: "#98CD80");

    public static readonly AppTheme Paper = Theme(
        "paper", "Paper", "Sepia page for long reading sessions", dark: false,
        background: "#F3EBDB", surface: "#FBF6EA", raised: "#E9DEC9", border: "#C9B999",
        text: "#2A2017", muted: "#5E5142",
        accent: "#155D70", onAccent: "#FFFFFF", secondary: "#814508",
        violet: "#573A9E", mint: "#0B645B", error: "#9C261B", warning: "#664B00", success: "#27632A");

    public static readonly AppTheme Blueprint = Theme(
        "blueprint", "Blueprint", "Navy drafting table", dark: true,
        background: "#0D192C", surface: "#12223A", raised: "#1A2F4D", border: "#30496F",
        text: "#E4EDFA", muted: "#A3B7D5",
        accent: "#74BAFF", onAccent: "#071326", secondary: "#F2AB60",
        violet: "#C6B3FF", mint: "#72E4D2", error: "#FF9797", warning: "#F3D871", success: "#84DB9E");

    public static readonly AppTheme Terminal = Theme(
        "terminal", "Terminal", "Green phosphor on black", dark: true,
        background: "#090E09", surface: "#0E160E", raised: "#162116", border: "#2C422C",
        text: "#CBF6C6", muted: "#8DBB88",
        accent: "#5FE56E", onAccent: "#041006", secondary: "#E8B856",
        violet: "#C8ADF6", mint: "#5CD6C2", error: "#FF857C", warning: "#F1D366", success: "#8FF09C");

    public static readonly AppTheme Nord = Theme(
        "nord", "Nord", "Cool arctic blue-grey", dark: true,
        background: "#242933", surface: "#2E3440", raised: "#3B4252", border: "#55607A",
        text: "#ECEFF4", muted: "#C3CAD8",
        accent: "#8FC6D6", onAccent: "#1D2530", secondary: "#EBCB8B",
        violet: "#D2BDE6", mint: "#A3D9C9", error: "#F2A9B0", warning: "#F0D79F", success: "#B5D5A0");

    /// <summary>In the order shown in the theme picker.</summary>
    public static IReadOnlyList<AppTheme> All { get; } =
        [Light, Dark, HighContrast, HighContrastLight, Neural, Circuit, Paper, Blueprint, Terminal, Nord];

    public static AppTheme? Find(string? id) =>
        All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static AppTheme Theme(
        string id, string name, string description, bool dark,
        string background, string surface, string raised, string border,
        string text, string muted,
        string accent, string onAccent, string secondary,
        string violet, string mint, string error, string warning, string success,
        bool highContrast = false) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            IsDark = dark,
            IsHighContrast = highContrast,
            Background = Color.Parse(background),
            Surface = Color.Parse(surface),
            SurfaceRaised = Color.Parse(raised),
            Border = Color.Parse(border),
            Text = Color.Parse(text),
            TextMuted = Color.Parse(muted),
            Accent = Color.Parse(accent),
            OnAccent = Color.Parse(onAccent),
            Secondary = Color.Parse(secondary),
            Violet = Color.Parse(violet),
            Mint = Color.Parse(mint),
            Error = Color.Parse(error),
            Warning = Color.Parse(warning),
            Success = Color.Parse(success),
        };
}
