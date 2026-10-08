using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotnetDiag.Core;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.ViewModels;

/// <summary>The Analyze page: choose an input and options, run the pipeline, show the report.</summary>
public sealed partial class AnalyzeViewModel : ObservableObject
{
    /// <summary>The options that decide which provider is used; changing one re-reads the configuration.</summary>
    private static readonly HashSet<string> ProviderProperties =
    [
        nameof(Provider), nameof(Model), nameof(Endpoint), nameof(ApiKey), nameof(Effort), nameof(TimeoutSeconds),
    ];

    /// <summary>The remaining options with a CLI flag; they only change the command preview.</summary>
    private static readonly HashSet<string> CommandProperties =
    [
        nameof(InputPath), nameof(PastedSummary), nameof(Repository), nameof(TokenBudget), nameof(ShowWarnings), nameof(ExitCodeGate),
    ];

    private readonly SettingsViewModel _settings;
    private readonly IDesktopServices _desktop;
    private readonly Func<AnalysisRequest, ProviderSettings> _resolveProvider;
    private string? _pastedText;
    private int _run;

    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string? _pastedSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiOptionsEnabled))]
    private Choice<ProviderKind?> _provider;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _endpoint = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private Choice<string?> _effort;

    [ObservableProperty]
    private decimal? _timeoutSeconds;

    [ObservableProperty]
    private string _repository = string.Empty;

    [ObservableProperty]
    private decimal? _tokenBudget;

    [ObservableProperty]
    private bool _showWarnings;

    [ObservableProperty]
    private bool _exitCodeGate;

    [ObservableProperty]
    private bool _optionsExpanded;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _providerWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private ReportViewModel? _report;

    [ObservableProperty]
    private string _commandPreview = string.Empty;

    [ObservableProperty]
    private string _providerSummary = string.Empty;

    [ObservableProperty]
    private Tone _providerTone = Tone.Neutral;

    public AnalyzeViewModel(
        SettingsViewModel settings,
        IDesktopServices desktop,
        Func<AnalysisRequest, ProviderSettings>? resolveProvider = null)
    {
        _settings = settings;
        _desktop = desktop;
        _resolveProvider = resolveProvider ?? AnalysisService.ResolveProvider;

        var defaults = settings.Current;
        _provider = ProviderChoices[defaults.AlwaysOffline ? 1 : 0];
        _effort = EffortChoices[0];
        _tokenBudget = defaults.TokenBudget;
        _showWarnings = defaults.ShowWarnings;
        _exitCodeGate = defaults.ExitCodeGate;
        RecentInputs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecent));
        settings.PropertyChanged += OnSettingsChanged;
        RefreshDerived();
    }

    /// <summary>The user asked for the offline explanation of a code shown in the report.</summary>
    public event Action<string>? ExplainRequested;

    public IReadOnlyList<Choice<ProviderKind?>> ProviderChoices { get; } =
    [
        new("From configuration", null),
        new("None (offline)", ProviderKind.None),
        new("Ollama (local model)", ProviderKind.Ollama),
        new("Anthropic", ProviderKind.Anthropic),
        new("OpenAI-compatible", ProviderKind.OpenAI),
    ];

    public IReadOnlyList<Choice<string?>> EffortChoices { get; } =
    [
        new("Default", null), new("low", "low"), new("medium", "medium"), new("high", "high"), new("xhigh", "xhigh"), new("max", "max"),
    ];

    public ObservableCollection<string> RecentInputs => _settings.RecentInputs;

    public bool HasRecent => RecentInputs.Count > 0;

    public bool HasReport => Report is not null;

    /// <summary>Model, endpoint and key only matter when a provider may be called.</summary>
    public bool AiOptionsEnabled => Provider.Value != ProviderKind.None;

    public AnalysisRequest BuildRequest() => new()
    {
        InputPath = _pastedText is null ? InputPath : null,
        PastedText = _pastedText,
        Repository = NullIfBlank(Repository),
        DefaultRepository = _settings.Current.DefaultRepository,
        Offline = Provider.Value == ProviderKind.None,
        Overrides = new ProviderOverrides(
            Provider.Value is ProviderKind.None ? null : Provider.Value,
            NullIfBlank(Model),
            NullIfBlank(Endpoint),
            NullIfBlank(ApiKey),
            (int?)TimeoutSeconds,
            MaxOutputTokens: null,
            Effort.Value),
        Context = _settings.Current.ToContextOptions((int)(TokenBudget ?? _settings.Current.TokenBudget)),
    };

    /// <summary>Opens a file or folder from a picker, a drop, the recent list or the command line.</summary>
    public async Task OpenPathAsync(string path)
    {
        SetPasted(null);
        InputPath = path;
        if (_settings.Current.AutoAnalyze)
        {
            await AnalyzeCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Analyzes log text directly: the counterpart of piping into <c>dotnet-diag analyze</c>.</summary>
    public async Task OpenTextAsync(string text)
    {
        SetPasted(text);
        if (_settings.Current.AutoAnalyze)
        {
            await AnalyzeCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Updates the command preview and, unless only the command changed, the provider line.</summary>
    public void RefreshDerived(bool provider = true)
    {
        var request = BuildRequest();
        CommandPreview = CliCommandBuilder.Build(request, ShowWarnings, ExitCodeGate);
        if (!provider)
        {
            return;
        }

        var settings = _resolveProvider(request);
        (ProviderSummary, ProviderTone) = settings switch
        {
            { Kind: ProviderKind.None } => ("AI off · local hints only", Tone.Neutral),
            { Problem: not null } => ($"{settings.Kind.DisplayName()} is not usable yet", Tone.Warning),
            _ => ($"{settings.Kind.DisplayName()} · {settings.Model}", Tone.Violet),
        };
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not { } name)
        {
            return;
        }

        if (ProviderProperties.Contains(name))
        {
            RefreshDerived();
        }
        else if (CommandProperties.Contains(name))
        {
            RefreshDerived(provider: false);
        }
    }

    partial void OnInputPathChanged(string value)
    {
        // Typing or opening a path replaces a pasted log.
        if (value.Length > 0)
        {
            SetPasted(null);
        }
    }

    partial void OnShowWarningsChanged(bool value)
    {
        if (Report is { } report)
        {
            report.ShowWarnings = value;
        }
    }

    partial void OnExitCodeGateChanged(bool value)
    {
        if (Report is { } report)
        {
            report.ExitCodeGate = value;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        var request = BuildRequest();
        var settings = _resolveProvider(request);
        var budget = request.Context.MaxTokens;
        var name = request.PastedText is not null
            ? AnalysisService.PastedSourceName
            : Path.GetFileName(InputPath.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Opening another log while one is being analyzed starts a new run and cancels the old one.
        // Only the newest run may touch the page, or the old one's cleanup would undo the new one's state.
        var run = ++_run;
        bool IsCurrent() => run == _run;

        ErrorMessage = null;
        ProviderWarning = null;
        IsBusy = true;
        StatusText = Describe(PipelineStage.Parsing, name, settings, budget);
        var progress = new Progress<PipelineStage>(stage =>
        {
            if (IsCurrent() && IsBusy)
            {
                StatusText = Describe(stage, name, settings, budget);
            }
        });

        try
        {
            var outcome = await AnalysisService.RunAsync(request, progress, cancellationToken);
            if (!IsCurrent())
            {
                return;
            }

            if (outcome.Result is null)
            {
                ErrorMessage = outcome.Error;
                StatusText = "Invalid input";
                return;
            }

            // Shaping the report maps every path in the log onto the working tree, which is disk work too.
            var providerProblem = settings.Kind != ProviderKind.None ? settings.Problem : null;
            var options = new ReportOptions(ShowWarnings, ExitCodeGate, providerProblem);
            var report = await Task.Run(
                () => new ReportViewModel(outcome.Result, options, _desktop, code => ExplainRequested?.Invoke(code)),
                cancellationToken);
            if (!IsCurrent())
            {
                return;
            }

            report.PropertyChanged += OnReportPropertyChanged;
            if (Report is { } previous)
            {
                previous.PropertyChanged -= OnReportPropertyChanged;
            }

            Report = report;
            ProviderWarning = providerProblem is null ? null : $"{providerProblem} Continuing with local hints only.";
            StatusText = report.StatusLine;

            if (request.PastedText is null && !string.IsNullOrWhiteSpace(request.InputPath))
            {
                _settings.AddRecent(Path.GetFullPath(request.InputPath.Trim().Trim('"')));
            }
        }
        catch (OperationCanceledException) when (IsCurrent())
        {
            StatusText = "Canceled";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer run, which owns the page now.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            // An unreadable or damaged file is the user's input being wrong, not the app: say so and stay usable.
            if (IsCurrent())
            {
                ErrorMessage = ex.Message;
                StatusText = "Could not read the input";
            }
        }
        catch (Exception ex)
        {
            // Logs are arbitrary input. A parser bug on a strange one should cost this analysis, not the whole
            // session: an exception leaving a command would end the process.
            if (IsCurrent())
            {
                ErrorMessage = $"The analysis failed unexpectedly ({ex.GetType().Name}): {ex.Message}";
                StatusText = "Analysis failed";
            }
        }
        finally
        {
            if (IsCurrent())
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        if (await _desktop.PickLogFileAsync() is { } path)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (await _desktop.PickFolderAsync("Open a folder that contains a .binlog") is { } path)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task BrowseRepositoryAsync()
    {
        if (await _desktop.PickFolderAsync("Choose the source tree the log refers to") is { } path)
        {
            Repository = path;
        }
    }

    /// <summary>Takes build or test output from the clipboard. A copied path is opened as a file instead.</summary>
    [RelayCommand]
    private async Task PasteAsync()
    {
        var text = await _desktop.ReadClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorMessage = "The clipboard has no text. Copy build or test output first, then paste it here.";
            return;
        }

        var single = text.Trim().Trim('"');
        if (!single.Contains('\n', StringComparison.Ordinal) && (File.Exists(single) || Directory.Exists(single)))
        {
            await OpenPathAsync(single);
        }
        else
        {
            await OpenTextAsync(text);
        }
    }

    [RelayCommand]
    private Task OpenRecentAsync(string? path) => string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : OpenPathAsync(path);

    [RelayCommand]
    private void ClearPasted() => SetPasted(null);

    /// <summary>Back to the start screen.</summary>
    [RelayCommand]
    private void CloseReport()
    {
        if (Report is { } report)
        {
            report.PropertyChanged -= OnReportPropertyChanged;
        }

        Report = null;
        ErrorMessage = null;
        ProviderWarning = null;
        StatusText = "Ready";
    }

    [RelayCommand]
    private Task CopyCommandAsync() => _desktop.CopyTextAsync(CommandPreview);

    private static string Describe(PipelineStage stage, string sourceName, ProviderSettings settings, int budget) => stage switch
    {
        PipelineStage.Parsing => $"Parsing {sourceName}…",
        PipelineStage.InspectingWorkspace => "Inspecting git working tree…",
        PipelineStage.Analyzing => "Matching known MSBuild, NuGet and compiler patterns…",
        PipelineStage.BuildingContext => $"Building a {budget}-token diagnostic bundle…",
        PipelineStage.QueryingModel => $"Asking {settings.Kind.DisplayName()} ({settings.Model ?? "?"}) for a diagnosis…",
        _ => "Working…",
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SetPasted(string? text)
    {
        _pastedText = text;
        if (text is null)
        {
            PastedSummary = null;
            return;
        }

        var lines = text.AsSpan().Count('\n') + 1;
        InputPath = string.Empty;
        PastedSummary = $"Pasted log · {lines} line(s)";
    }

    /// <summary>A default changed in Settings: it applies to this page at once, not after a restart.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.AlwaysOffline) when _settings.AlwaysOffline:
                Provider = ProviderChoices[1];
                break;
            case nameof(SettingsViewModel.AlwaysOffline) when Provider.Value == ProviderKind.None:
                Provider = ProviderChoices[0];
                break;
            case nameof(SettingsViewModel.TokenBudget) when _settings.TokenBudget is { } budget:
                TokenBudget = budget;
                break;
            case nameof(SettingsViewModel.ShowWarnings):
                ShowWarnings = _settings.ShowWarnings;
                break;
            case nameof(SettingsViewModel.ExitCodeGate):
                ExitCodeGate = _settings.ExitCodeGate;
                break;
        }
    }

    /// <summary>The report has its own warnings toggle; keep the run option in step with it.</summary>
    private void OnReportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportViewModel.ShowWarnings) && sender is ReportViewModel report)
        {
            ShowWarnings = report.ShowWarnings;
        }
    }
}
