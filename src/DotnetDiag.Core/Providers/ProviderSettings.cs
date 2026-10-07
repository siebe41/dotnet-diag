namespace DotnetDiag.Core.Providers;

/// <summary>Fully resolved provider configuration (flags → environment → config file → defaults).</summary>
public sealed record ProviderSettings
{
    public ProviderKind Kind { get; init; } = ProviderKind.None;

    public string? Model { get; init; }

    public Uri? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>Upper bound for the model's response. Null lets the provider pick its default.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Anthropic <c>output_config.effort</c> (low/medium/high/xhigh/max). Null uses the provider default.</summary>
    public string? Effort { get; init; }

    public string? ConfigFilePath { get; init; }

    public bool ConfigFileFound { get; init; }

    /// <summary>Where each value came from (e.g. "Model" → "DOTNET_DIAG_MODEL"), for <c>dotnet-diag doctor</c>.</summary>
    public IReadOnlyDictionary<string, string> Origins { get; init; } = new Dictionary<string, string>();

    /// <summary>Why the provider cannot be used (missing key, missing model), or null when usable.</summary>
    public string? Problem { get; init; }

    public bool IsEnabled => Kind != ProviderKind.None && Problem is null;
}

/// <summary>Values supplied on the command line; null means "not specified".</summary>
public sealed record ProviderOverrides(
    ProviderKind? Kind = null,
    string? Model = null,
    string? Endpoint = null,
    string? ApiKey = null,
    int? TimeoutSeconds = null,
    int? MaxOutputTokens = null,
    string? Effort = null);
