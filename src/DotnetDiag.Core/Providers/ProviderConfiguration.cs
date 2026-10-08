namespace DotnetDiag.Core.Providers;

/// <summary>
/// Resolves provider settings. Precedence: command-line flags, then environment variables, then the user
/// config file, then built-in defaults. An AI provider is only used when explicitly selected (flag,
/// DOTNET_DIAG_PROVIDER or config file) — an API key in the environment alone never sends code anywhere.
/// </summary>
public static class ProviderConfiguration
{
    public const string DefaultAnthropicModel = "claude-opus-5-5";
    public const string DefaultOllamaModel = "llama3.1";
    public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";
    public const string DefaultOpenAIEndpoint = "https://api.openai.com/v1";
    public const string DefaultOllamaEndpoint = "http://localhost:11434";

    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "dotnet-diag", "config.json");

    public static ProviderSettings Resolve(
        ProviderOverrides? overrides = null,
        Func<string, string?>? environment = null,
        string? configPath = null)
    {
        overrides ??= new ProviderOverrides();
        environment ??= Environment.GetEnvironmentVariable;
        configPath ??= environment("DOTNET_DIAG_CONFIG") is { Length: > 0 } envPath ? envPath : DefaultConfigPath;

        var file = UserConfigFile.Load(configPath);
        var origins = new Dictionary<string, string>();

        T? Pick<T>(string name, T? flag, string? envName, Func<string, T?>? fromEnv, T? fromFile)
        {
            if (flag is not null)
            {
                origins[name] = "command line";
                return flag;
            }

            if (envName is not null && fromEnv is not null && environment(envName) is { Length: > 0 } envValue && fromEnv(envValue) is { } parsed)
            {
                origins[name] = envName;
                return parsed;
            }

            if (fromFile is not null)
            {
                origins[name] = "config file";
                return fromFile;
            }

            return default;
        }

        ProviderKind? ParseKind(string value) => ProviderKinds.TryParse(value, out var k) ? k : null;
        ProviderKind? fileKind = file?.Provider is { } fp && ProviderKinds.TryParse(fp, out var fk) ? fk : null;
        var kind = Pick("Provider", overrides.Kind, "DOTNET_DIAG_PROVIDER", ParseKind, fileKind) ?? ProviderKind.None;
        if (!origins.ContainsKey("Provider"))
        {
            origins["Provider"] = "default";
        }

        var model = Pick("Model", overrides.Model, "DOTNET_DIAG_MODEL", v => v, file?.Model);
        var endpoint = Pick("Endpoint", overrides.Endpoint, "DOTNET_DIAG_ENDPOINT", v => v, file?.Endpoint);
        var apiKey = Pick("ApiKey", overrides.ApiKey, "DOTNET_DIAG_API_KEY", v => v, file?.ApiKey);
        var timeout = Pick("Timeout", overrides.TimeoutSeconds, "DOTNET_DIAG_TIMEOUT", v => int.TryParse(v, out var s) ? s : (int?)null, file?.TimeoutSeconds);
        var maxTokens = Pick("MaxOutputTokens", overrides.MaxOutputTokens, "DOTNET_DIAG_MAX_OUTPUT_TOKENS", v => int.TryParse(v, out var s) ? s : (int?)null, file?.MaxOutputTokens);
        var effort = Pick("Effort", overrides.Effort, "DOTNET_DIAG_EFFORT", v => v, file?.Effort);

        string? problem = null;
        switch (kind)
        {
            case ProviderKind.Anthropic:
                apiKey ??= FromEnv("ANTHROPIC_API_KEY", "ApiKey");
                endpoint ??= FromEnv("ANTHROPIC_BASE_URL", "Endpoint") ?? Default("Endpoint", DefaultAnthropicEndpoint);
                model ??= Default("Model", DefaultAnthropicModel);
                if (string.IsNullOrEmpty(apiKey))
                {
                    problem = "Anthropic needs an API key: set ANTHROPIC_API_KEY (or --api-key / DOTNET_DIAG_API_KEY).";
                }

                break;

            case ProviderKind.OpenAI:
                apiKey ??= FromEnv("OPENAI_API_KEY", "ApiKey");
                endpoint ??= FromEnv("OPENAI_BASE_URL", "Endpoint") ?? Default("Endpoint", DefaultOpenAIEndpoint);
                if (string.IsNullOrEmpty(model))
                {
                    problem = "The OpenAI-compatible provider needs a model: pass --model or set DOTNET_DIAG_MODEL.";
                }
                else if (string.IsNullOrEmpty(apiKey) && IsHost(endpoint, "api.openai.com"))
                {
                    problem = "api.openai.com needs an API key: set OPENAI_API_KEY (or --api-key / DOTNET_DIAG_API_KEY).";
                }

                break;

            case ProviderKind.Ollama:
                endpoint ??= FromEnv("OLLAMA_HOST", "Endpoint") ?? Default("Endpoint", DefaultOllamaEndpoint);
                model ??= Default("Model", DefaultOllamaModel);
                break;
        }

        Uri? endpointUri = null;
        if (kind != ProviderKind.None && endpoint is not null)
        {
            endpointUri = NormalizeEndpoint(endpoint);
            if (endpointUri is null)
            {
                problem ??= $"'{endpoint}' is not a valid endpoint URL.";
            }
        }

        return new ProviderSettings
        {
            Kind = kind,
            Model = kind == ProviderKind.None ? null : model,
            Endpoint = endpointUri,
            ApiKey = kind == ProviderKind.None ? null : apiKey,
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeout ?? 180, 5, 3600)),
            MaxOutputTokens = maxTokens is > 0 ? maxTokens : null,
            Effort = effort,
            ConfigFilePath = configPath,
            ConfigFileFound = file is not null,
            Origins = origins,
            Problem = problem,
        };

        string? FromEnv(string name, string key)
        {
            var value = environment(name);
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            origins[key] = name;
            return value;
        }

        string Default(string key, string value)
        {
            origins[key] = "default";
            return value;
        }
    }

    /// <summary>Accepts "host:port", "http://host:port" and OLLAMA_HOST-style "0.0.0.0:11434".</summary>
    public static Uri? NormalizeEndpoint(string endpoint)
    {
        var value = endpoint.Trim().TrimEnd('/');
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (uri.Host is "0.0.0.0" or "[::]")
        {
            uri = new UriBuilder(uri) { Host = "localhost" }.Uri;
        }

        return uri;
    }

    private static bool IsHost(string? endpoint, string host) =>
        endpoint is not null && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase);
}
