using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.ViewModels;

public sealed record DoctorRow(string Setting, string Value, string Source, Tone Tone = Tone.None);

public sealed record VariableRow(string Name, string Purpose, string Value, bool IsSet);

/// <summary>The Doctor page: the resolved provider configuration and where each value comes from (<c>dotnet-diag doctor</c>).</summary>
public sealed partial class DoctorViewModel : ObservableObject
{
    /// <summary>Every environment variable <see cref="ProviderConfiguration"/> reads. Secrets are never displayed.</summary>
    private static readonly (string Name, string Purpose, bool Secret)[] KnownVariables =
    [
        ("DOTNET_DIAG_PROVIDER", "ollama, anthropic, openai or none", false),
        ("DOTNET_DIAG_MODEL", "Model ID", false),
        ("DOTNET_DIAG_ENDPOINT", "Provider base URL", false),
        ("DOTNET_DIAG_API_KEY", "API key for any provider", true),
        ("DOTNET_DIAG_EFFORT", "Anthropic effort level", false),
        ("DOTNET_DIAG_TIMEOUT", "Request timeout in seconds", false),
        ("DOTNET_DIAG_MAX_OUTPUT_TOKENS", "Upper bound for the model's answer", false),
        ("DOTNET_DIAG_CONFIG", "Config file to use instead of the default", false),
        ("ANTHROPIC_API_KEY", "Key used when the provider is Anthropic", true),
        ("ANTHROPIC_BASE_URL", "Endpoint used when the provider is Anthropic", false),
        ("OPENAI_API_KEY", "Key used when the provider is OpenAI-compatible", true),
        ("OPENAI_BASE_URL", "Endpoint used when the provider is OpenAI-compatible", false),
        ("OLLAMA_HOST", "Endpoint used when the provider is Ollama", false),
    ];

    private readonly Func<AnalysisRequest> _request;
    private readonly IDesktopServices _desktop;
    private readonly Func<AnalysisRequest, ProviderSettings> _resolve;
    private readonly Func<string, string?> _environment;
    private string? _configPath;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private Tone _statusTone = Tone.Neutral;

    /// <param name="request">The current run options: they rank above everything else, like flags on the command line.</param>
    public DoctorViewModel(
        Func<AnalysisRequest> request,
        IDesktopServices desktop,
        Func<AnalysisRequest, ProviderSettings>? resolve = null,
        Func<string, string?>? environment = null)
    {
        _request = request;
        _desktop = desktop;
        _resolve = resolve ?? AnalysisService.ResolveProvider;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        Refresh();
    }

    /// <summary>The user wants to change what this page shows.</summary>
    public event Action? SettingsRequested;

    public ObservableCollection<DoctorRow> Rows { get; } = [];

    public ObservableCollection<VariableRow> Variables { get; } = [];

    [RelayCommand]
    public void Refresh()
    {
        var request = _request();
        var settings = _resolve(request);
        _configPath = settings.ConfigFilePath;

        // "command line" is what Core calls the highest-ranking source; here those values come from the Analyze page.
        string Origin(string key) => settings.Origins.TryGetValue(key, out var origin)
            ? origin == "command line" ? "run options" : origin
            : "-";

        Rows.Clear();
        Rows.Add(new DoctorRow("Provider", settings.Kind.DisplayName(), request.Offline ? "run options" : Origin("Provider")));
        if (settings.Kind != ProviderKind.None)
        {
            Rows.Add(new DoctorRow("Model", settings.Model ?? "(not set)", Origin("Model"), settings.Model is null ? Tone.Warning : Tone.None));
            Rows.Add(new DoctorRow("Endpoint", settings.Endpoint is null ? "(not set)" : WithoutCredentials(settings.Endpoint.ToString()), Origin("Endpoint")));
            Rows.Add(string.IsNullOrEmpty(settings.ApiKey)
                ? new DoctorRow("API key", "not set", Origin("ApiKey"), Tone.Neutral)
                : new DoctorRow("API key", "set", Origin("ApiKey"), Tone.Success));
            Rows.Add(new DoctorRow("Timeout", $"{settings.Timeout.TotalSeconds:0}s", Origin("Timeout") is "-" ? "default" : Origin("Timeout")));
            if (settings.Effort is not null)
            {
                Rows.Add(new DoctorRow("Effort", settings.Effort, Origin("Effort")));
            }

            if (settings.MaxOutputTokens is { } maxOutput)
            {
                Rows.Add(new DoctorRow("Max output tokens", maxOutput.ToString(System.Globalization.CultureInfo.InvariantCulture), Origin("MaxOutputTokens")));
            }
        }

        Rows.Add(settings.ConfigFileFound
            ? new DoctorRow("Config file", settings.ConfigFilePath ?? "-", "found", Tone.Success)
            : new DoctorRow("Config file", settings.ConfigFilePath ?? "-", "not found", Tone.Neutral));

        // The same file the analysis would read, so a folder input reports the workspace around its newest binlog.
        var workspace = AnalysisService.ResolveWorkspace(request, AnalysisService.ResolveInput(request).Path);
        Rows.Add(new DoctorRow("Git workspace", workspace?.Root ?? "(none)", workspace?.Branch ?? "-"));

        (Status, StatusTone) = settings switch
        {
            { Kind: ProviderKind.None } =>
                ("No AI provider selected: analyses use the built-in offline hints. Choose one under Run options, in Settings, or with DOTNET_DIAG_PROVIDER.", Tone.Neutral),
            { Problem: { } problem } => (problem, Tone.Warning),
            _ => ("Provider configuration looks usable.", Tone.Success),
        };

        Variables.Clear();
        foreach (var (name, purpose, secret) in KnownVariables)
        {
            var value = _environment(name);
            var isSet = !string.IsNullOrEmpty(value);
            Variables.Add(new VariableRow(name, purpose, !isSet ? "not set" : secret ? "set (hidden)" : WithoutCredentials(value!), isSet));
        }
    }

    /// <summary>Hides <c>user:password@</c> in a URL: this page is made to be copied into bug reports.</summary>
    private static string WithoutCredentials(string value) => CredentialMasking.WithoutCredentials(value);

    [RelayCommand]
    private Task CopyReportAsync()
    {
        var text = new StringBuilder("dotnet-diag configuration\n");
        foreach (var row in Rows)
        {
            text.Append($"{row.Setting,-18} {row.Value}  [{row.Source}]\n");
        }

        text.Append('\n').Append(Status).Append('\n');
        return _desktop.CopyTextAsync(text.ToString());
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        if (_configPath is not null && Path.GetDirectoryName(_configPath) is { } folder)
        {
            _desktop.OpenFolder(folder);
        }
    }

    [RelayCommand]
    private void EditSettings() => SettingsRequested?.Invoke();
}
