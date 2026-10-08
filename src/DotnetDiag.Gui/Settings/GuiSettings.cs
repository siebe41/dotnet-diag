using System.Text.Json;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.Settings;

/// <summary>
/// Preferences that only concern the desktop app (<c>gui.json</c>). Provider settings are not here: they live
/// in the <c>config.json</c> the CLI reads too, so both front ends always agree.
/// </summary>
public sealed record GuiSettings
{
    public const int MaxRecentInputs = 10;

    private static readonly ContextBuilderOptions ContextDefaults = new();

    // Appearance
    public string Theme { get; init; } = ThemeManager.SystemThemeId;

    public double TextScale { get; init; } = 1;

    public bool CompactDensity { get; init; }

    // Defaults for a new analysis
    public int TokenBudget { get; init; } = ContextDefaults.MaxTokens;

    public bool ShowWarnings { get; init; }

    /// <summary>Start every analysis with the provider set to "none", whatever the configuration says.</summary>
    public bool AlwaysOffline { get; init; }

    public bool ExitCodeGate { get; init; }

    public int MaxSnippets { get; init; } = ContextDefaults.MaxSnippets;

    public int SnippetRadius { get; init; } = ContextDefaults.SnippetRadius;

    public int MaxFramesPerTest { get; init; } = ContextDefaults.MaxFramesPerTest;

    /// <summary>Analyze as soon as a log is opened, dropped or pasted.</summary>
    public bool AutoAnalyze { get; init; } = true;

    public string? DefaultRepository { get; init; }

    // History and window
    public bool RememberRecent { get; init; } = true;

    public IReadOnlyList<string> RecentInputs { get; init; } = [];

    public double WindowWidth { get; init; } = 1280;

    public double WindowHeight { get; init; } = 840;

    public bool WindowMaximized { get; init; }

    /// <summary>Clamps hand-edited or outdated values into their valid ranges.</summary>
    public GuiSettings Normalized() => this with
    {
        Theme = string.IsNullOrWhiteSpace(Theme) ? ThemeManager.SystemThemeId : Theme,
        TextScale = double.IsFinite(TextScale) ? Math.Clamp(TextScale, ThemeManager.MinTextScale, ThemeManager.MaxTextScale) : 1,
        TokenBudget = Math.Clamp(TokenBudget, ContextBuilderOptions.MinTokenBudget, ContextBuilderOptions.MaxTokenBudget),
        MaxSnippets = Math.Clamp(MaxSnippets, 0, 10),
        SnippetRadius = Math.Clamp(SnippetRadius, 0, 10),
        MaxFramesPerTest = Math.Clamp(MaxFramesPerTest, 1, 10),
        DefaultRepository = string.IsNullOrWhiteSpace(DefaultRepository) ? null : DefaultRepository,
        RecentInputs = RememberRecent ? [.. (RecentInputs ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxRecentInputs)] : [],
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 720, 10_000) : 1280,
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 480, 10_000) : 840,
    };

    /// <summary>Moves <paramref name="path"/> to the front of the recent list.</summary>
    public GuiSettings WithRecent(string path)
    {
        if (!RememberRecent)
        {
            return this;
        }

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return this with
        {
            RecentInputs = [path, .. RecentInputs.Where(p => !p.Equals(path, comparison)).Take(MaxRecentInputs - 1)],
        };
    }

    public ContextBuilderOptions ToContextOptions(int tokenBudget) => new()
    {
        MaxTokens = tokenBudget,
        MaxSnippets = MaxSnippets,
        SnippetRadius = SnippetRadius,
        MaxFramesPerTest = MaxFramesPerTest,
    };
}

/// <summary>Loads and saves <see cref="GuiSettings"/>. A missing or damaged file simply yields the defaults.</summary>
public sealed class GuiSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Path { get; } = path;

    /// <summary><c>gui.json</c> beside the CLI's config file, or the path in <c>DOTNET_DIAG_GUI_SETTINGS</c>.</summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("DOTNET_DIAG_GUI_SETTINGS") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ProviderConfiguration.DefaultConfigPath)!, "gui.json");

    public GuiSettings Load()
    {
        try
        {
            if (File.Exists(Path) && JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(Path), Options) is { } settings)
            {
                return settings.Normalized();
            }
        }
        catch (JsonException)
        {
            // Hand-edited into something unreadable. The next save replaces the file, so keep what was there.
            try
            {
                File.Copy(Path, Path + ".bak", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return new GuiSettings();
    }

    /// <summary>Returns false when the file could not be written (read-only profile, full disk).</summary>
    public bool TrySave(GuiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings.Normalized(), Options) + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
