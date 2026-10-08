using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.ViewModels;

/// <summary>A tile in the theme picker, with the colors needed to draw its preview.</summary>
public sealed record ThemeChoice(string Id, string Name, string Description, AppTheme Preview)
{
    public IBrush Background { get; } = new ImmutableSolidColorBrush(Preview.Background);

    public IBrush Surface { get; } = new ImmutableSolidColorBrush(Preview.Surface);

    public IBrush Border { get; } = new ImmutableSolidColorBrush(Preview.Border);

    public IBrush Text { get; } = new ImmutableSolidColorBrush(Preview.Text);

    public IBrush TextMuted { get; } = new ImmutableSolidColorBrush(Preview.TextMuted);

    public IBrush Accent { get; } = new ImmutableSolidColorBrush(Preview.Accent);

    public IBrush Secondary { get; } = new ImmutableSolidColorBrush(Preview.Secondary);

    public IBrush Error { get; } = new ImmutableSolidColorBrush(Preview.Error);

    public IBrush Success { get; } = new ImmutableSolidColorBrush(Preview.Success);
}

/// <summary>The Settings page: appearance, analysis defaults and the provider configuration shared with the CLI.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly HashSet<string> AppearanceProperties = [nameof(SelectedTheme), nameof(TextScale), nameof(CompactDensity)];

    private static readonly HashSet<string> PreferenceProperties =
    [
        nameof(TokenBudget), nameof(ShowWarnings), nameof(AlwaysOffline), nameof(ExitCodeGate), nameof(MaxSnippets),
        nameof(SnippetRadius), nameof(MaxFramesPerTest), nameof(AutoAnalyze), nameof(DefaultRepository), nameof(RememberRecent),
    ];

    private static readonly HashSet<string> ProviderProperties =
    [
        nameof(ConfigProvider), nameof(ConfigModel), nameof(ConfigEndpoint), nameof(ConfigApiKey), nameof(ConfigTimeout),
        nameof(ConfigMaxOutputTokens), nameof(ConfigEffort),
    ];

    private readonly GuiSettingsStore _store;
    private readonly IDesktopServices _desktop;
    private readonly HashSet<string> _editedProviderFields = [];
    private bool _loading;

    [ObservableProperty]
    private ThemeChoice _selectedTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextScaleLabel))]
    private double _textScale;

    [ObservableProperty]
    private bool _compactDensity;

    [ObservableProperty]
    private decimal? _tokenBudget;

    [ObservableProperty]
    private bool _showWarnings;

    [ObservableProperty]
    private bool _alwaysOffline;

    [ObservableProperty]
    private bool _exitCodeGate;

    [ObservableProperty]
    private decimal? _maxSnippets;

    [ObservableProperty]
    private decimal? _snippetRadius;

    [ObservableProperty]
    private decimal? _maxFramesPerTest;

    [ObservableProperty]
    private bool _autoAnalyze;

    [ObservableProperty]
    private string _defaultRepository = string.Empty;

    [ObservableProperty]
    private bool _rememberRecent;

    [ObservableProperty]
    private string? _preferencesStatus;

    [ObservableProperty]
    private Choice<string?> _configProvider;

    [ObservableProperty]
    private string _configModel = string.Empty;

    [ObservableProperty]
    private string _configEndpoint = string.Empty;

    [ObservableProperty]
    private string _configApiKey = string.Empty;

    [ObservableProperty]
    private decimal? _configTimeout;

    [ObservableProperty]
    private decimal? _configMaxOutputTokens;

    [ObservableProperty]
    private Choice<string?> _configEffort;

    [ObservableProperty]
    private bool _isProviderDirty;

    [ObservableProperty]
    private string? _providerStatus;

    [ObservableProperty]
    private Tone _providerStatusTone = Tone.Neutral;

    /// <param name="configPath">The provider config file; by default the one the CLI resolves.</param>
    /// <param name="systemTheme">What "System" currently resolves to, for its preview tile.</param>
    public SettingsViewModel(GuiSettingsStore store, IDesktopServices desktop, string? configPath = null, Func<AppTheme>? systemTheme = null)
    {
        _store = store;
        _desktop = desktop;
        ConfigPath = configPath ?? ProviderConfiguration.Resolve().ConfigFilePath ?? ProviderConfiguration.DefaultConfigPath;

        var system = systemTheme?.Invoke() ?? ThemeManager.Resolve(ThemeManager.SystemThemeId);
        Themes =
        [
            new ThemeChoice(ThemeManager.SystemThemeId, "System", "Follows your operating system", system),
            .. ThemeCatalog.All.Select(theme => new ThemeChoice(theme.Id, theme.Name, theme.Description, theme)),
        ];

        Current = store.Load();
        _selectedTheme = Themes[0];
        _configProvider = ProviderChoices[0];
        _configEffort = EffortChoices[0];
        Show(Current);
        LoadProviderConfig();
    }

    /// <summary>Theme, text size or density changed: the window needs to restyle itself.</summary>
    public event Action? AppearanceChanged;

    /// <summary>The config file the CLI shares was rewritten.</summary>
    public event Action? ProviderConfigSaved;

    /// <summary>The settings as last saved.</summary>
    public GuiSettings Current { get; private set; }

    public string SettingsPath => _store.Path;

    public string ConfigPath { get; }

    public IReadOnlyList<ThemeChoice> Themes { get; }

    public ObservableCollection<string> RecentInputs { get; } = [];

    public double MinTextScale => ThemeManager.MinTextScale;

    public double MaxTextScale => ThemeManager.MaxTextScale;

    public string TextScaleLabel => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{TextScale * 100:0}%");

    public int MinTokenBudget => ContextBuilderOptions.MinTokenBudget;

    public int MaxTokenBudget => ContextBuilderOptions.MaxTokenBudget;

    public IReadOnlyList<Choice<string?>> ProviderChoices { get; } =
    [
        new("Not set", null), new("None", "none"), new("Ollama (local model)", "ollama"), new("Anthropic", "anthropic"),
        new("OpenAI-compatible", "openai"),
    ];

    public IReadOnlyList<Choice<string?>> EffortChoices { get; } =
    [
        new("Not set", null), new("low", "low"), new("medium", "medium"), new("high", "high"), new("xhigh", "xhigh"), new("max", "max"),
    ];

    public void AddRecent(string path)
    {
        Current = Current.WithRecent(path);
        Save();
    }

    /// <summary>Remembers the window placement for the next start.</summary>
    public void SaveWindow(double width, double height, bool maximized)
    {
        Current = Current with { WindowWidth = width, WindowHeight = height, WindowMaximized = maximized };
        Save();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is not { } name)
        {
            return;
        }

        if (AppearanceProperties.Contains(name))
        {
            Persist();
            AppearanceChanged?.Invoke();
        }
        else if (PreferenceProperties.Contains(name))
        {
            Persist();
        }
        else if (ProviderProperties.Contains(name))
        {
            _editedProviderFields.Add(name);
            IsProviderDirty = true;
            ProviderStatus = null;
        }
    }

    [RelayCommand]
    private async Task BrowseDefaultRepositoryAsync()
    {
        if (await _desktop.PickFolderAsync("Choose the default source tree") is { } path)
        {
            DefaultRepository = path;
        }
    }

    [RelayCommand]
    private void ClearRecent()
    {
        Current = Current with { RecentInputs = [] };
        Save();
    }

    /// <summary>Back to the defaults for everything on this page except the provider configuration.</summary>
    [RelayCommand]
    private void ResetPreferences()
    {
        var defaults = new GuiSettings();
        Current = defaults with
        {
            RecentInputs = Current.RecentInputs,
            WindowWidth = Current.WindowWidth,
            WindowHeight = Current.WindowHeight,
            WindowMaximized = Current.WindowMaximized,
        };
        Show(Current);
        Save();
        AppearanceChanged?.Invoke();
        PreferencesStatus = "Preferences reset to their defaults.";
    }

    [RelayCommand]
    private void SaveProviderConfig()
    {
        bool Edited(string property) => _editedProviderFields.Contains(property);

        var endpoint = NullIfBlank(ConfigEndpoint);
        if (Edited(nameof(ConfigEndpoint)) && endpoint is not null && ProviderConfiguration.NormalizeEndpoint(endpoint) is null)
        {
            (ProviderStatus, ProviderStatusTone) = ($"'{endpoint}' is not a valid http(s) URL. Nothing was saved.", Tone.Error);
            return;
        }

        // Start from what is on disk now, not from what this page loaded: the CLI or an editor may have changed
        // the file since. A file that cannot be read is left alone, never replaced by a fresh one.
        UserConfigFile onDisk;
        if (!File.Exists(ConfigPath))
        {
            onDisk = new UserConfigFile();
        }
        else if (UserConfigFile.Load(ConfigPath) is { } readable)
        {
            onDisk = readable;
        }
        else
        {
            (ProviderStatus, ProviderStatusTone) = (UnreadableConfigMessage + " Nothing was saved.", Tone.Error);
            return;
        }

        // Only the fields edited here are written. Everything else keeps its value on disk, including values
        // this page cannot show, such as a provider name it does not recognize or an alias like "claude".
        var file = onDisk with
        {
            Provider = Edited(nameof(ConfigProvider)) ? ConfigProvider.Value : onDisk.Provider,
            Model = Edited(nameof(ConfigModel)) ? NullIfBlank(ConfigModel) : onDisk.Model,
            Endpoint = Edited(nameof(ConfigEndpoint)) ? endpoint : onDisk.Endpoint,
            ApiKey = Edited(nameof(ConfigApiKey)) ? NullIfBlank(ConfigApiKey) : onDisk.ApiKey,
            TimeoutSeconds = Edited(nameof(ConfigTimeout)) ? (int?)ConfigTimeout : onDisk.TimeoutSeconds,
            MaxOutputTokens = Edited(nameof(ConfigMaxOutputTokens)) ? (int?)ConfigMaxOutputTokens : onDisk.MaxOutputTokens,
            Effort = Edited(nameof(ConfigEffort)) ? ConfigEffort.Value : onDisk.Effort,
        };

        try
        {
            file.Save(ConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (ProviderStatus, ProviderStatusTone) = ($"Could not save: {ex.Message}", Tone.Error);
            return;
        }

        LoadProviderConfig();
        (ProviderStatus, ProviderStatusTone) = ("Saved. The command-line tool uses the same file.", Tone.Success);
        ProviderConfigSaved?.Invoke();
    }

    [RelayCommand]
    private void RevertProviderConfig() => LoadProviderConfig();

    [RelayCommand]
    private void OpenConfigFolder()
    {
        if (Path.GetDirectoryName(ConfigPath) is not { } folder)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            _desktop.OpenFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (ProviderStatus, ProviderStatusTone) = ($"Could not open {folder}: {ex.Message}", Tone.Error);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Show(GuiSettings settings)
    {
        _loading = true;
        try
        {
            SelectedTheme = Themes.FirstOrDefault(t => t.Id.Equals(settings.Theme, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
            TextScale = settings.TextScale;
            CompactDensity = settings.CompactDensity;
            TokenBudget = settings.TokenBudget;
            ShowWarnings = settings.ShowWarnings;
            AlwaysOffline = settings.AlwaysOffline;
            ExitCodeGate = settings.ExitCodeGate;
            MaxSnippets = settings.MaxSnippets;
            SnippetRadius = settings.SnippetRadius;
            MaxFramesPerTest = settings.MaxFramesPerTest;
            AutoAnalyze = settings.AutoAnalyze;
            DefaultRepository = settings.DefaultRepository ?? string.Empty;
            RememberRecent = settings.RememberRecent;
            SyncRecent();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Builds the settings from the controls; a cleared number box keeps its previous value.</summary>
    private void Persist()
    {
        Current = (Current with
        {
            Theme = SelectedTheme?.Id ?? Current.Theme, // a list can momentarily select nothing
            TextScale = TextScale,
            CompactDensity = CompactDensity,
            TokenBudget = (int?)TokenBudget ?? Current.TokenBudget,
            ShowWarnings = ShowWarnings,
            AlwaysOffline = AlwaysOffline,
            ExitCodeGate = ExitCodeGate,
            MaxSnippets = (int?)MaxSnippets ?? Current.MaxSnippets,
            SnippetRadius = (int?)SnippetRadius ?? Current.SnippetRadius,
            MaxFramesPerTest = (int?)MaxFramesPerTest ?? Current.MaxFramesPerTest,
            AutoAnalyze = AutoAnalyze,
            DefaultRepository = NullIfBlank(DefaultRepository),
            RememberRecent = RememberRecent,
        }).Normalized();
        Save();
    }

    private void Save()
    {
        PreferencesStatus = _store.TrySave(Current) ? null : $"Could not write {_store.Path}; changes last only for this session.";
        SyncRecent();
    }

    private void SyncRecent()
    {
        if (!RecentInputs.SequenceEqual(Current.RecentInputs))
        {
            RecentInputs.Clear();
            foreach (var path in Current.RecentInputs)
            {
                RecentInputs.Add(path);
            }
        }
    }

    private string UnreadableConfigMessage =>
        $"{ConfigPath} exists but is not valid JSON, so it is ignored. Fix or delete it; this page will not overwrite it.";

    private void LoadProviderConfig()
    {
        var onDisk = UserConfigFile.Load(ConfigPath);
        var unreadable = onDisk is null && File.Exists(ConfigPath);
        var config = onDisk ?? new UserConfigFile();

        _loading = true;
        try
        {
            // The file accepts aliases such as "claude"; show the canonical name.
            var provider = ProviderKinds.TryParse(config.Provider, out var kind) ? kind.ToString().ToLowerInvariant() : null;
            ConfigProvider = ProviderChoices.FirstOrDefault(c => c.Value == provider) ?? ProviderChoices[0];
            ConfigModel = config.Model ?? string.Empty;
            ConfigEndpoint = config.Endpoint ?? string.Empty;
            ConfigApiKey = config.ApiKey ?? string.Empty;
            ConfigTimeout = config.TimeoutSeconds;
            ConfigMaxOutputTokens = config.MaxOutputTokens;
            ConfigEffort = EffortChoices.FirstOrDefault(c => c.Value == config.Effort?.ToLowerInvariant()) ?? EffortChoices[0];
            IsProviderDirty = false;
            (ProviderStatus, ProviderStatusTone) = unreadable ? (UnreadableConfigMessage, Tone.Warning) : (null, Tone.Neutral);
        }
        finally
        {
            _loading = false;
            _editedProviderFields.Clear();
        }
    }
}
