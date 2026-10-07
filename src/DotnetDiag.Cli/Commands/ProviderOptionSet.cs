using System.CommandLine;
using DotnetDiag.Core.Providers;

namespace DotnetDiag.Cli.Commands;

/// <summary>The AI provider options shared by <c>analyze</c> and <c>doctor</c>.</summary>
internal sealed class ProviderOptionSet
{
    public Option<string?> Provider { get; } = new("--provider", "-p")
    {
        Description = "AI backend: none, ollama, anthropic or openai (any OpenAI-compatible endpoint). Default: DOTNET_DIAG_PROVIDER, the config file, or none.",
        HelpName = "name",
    };

    public Option<string?> Model { get; } = new("--model", "-m")
    {
        Description = $"Model id (defaults: anthropic={ProviderConfiguration.DefaultAnthropicModel}, ollama={ProviderConfiguration.DefaultOllamaModel}; required for openai).",
        HelpName = "id",
    };

    public Option<string?> Endpoint { get; } = new("--endpoint")
    {
        Description = "Base URL of the provider (e.g. http://localhost:11434, https://my-gateway/v1).",
        HelpName = "url",
    };

    public Option<string?> ApiKey { get; } = new("--api-key")
    {
        Description = "API key. Prefer ANTHROPIC_API_KEY / OPENAI_API_KEY / DOTNET_DIAG_API_KEY so the key stays out of shell history.",
        HelpName = "key",
    };

    public Option<int?> Timeout { get; } = new("--timeout")
    {
        Description = "Seconds to wait for the model (default 180).",
        HelpName = "seconds",
    };

    public Option<string?> Effort { get; } = new("--effort")
    {
        Description = "Anthropic effort level: low, medium, high, xhigh or max (default medium on current models).",
        HelpName = "level",
    };

    public ProviderOptionSet()
    {
        Provider.AcceptOnlyFromAmong("none", "ollama", "anthropic", "openai");
        Effort.AcceptOnlyFromAmong("low", "medium", "high", "xhigh", "max");
    }

    public void AddTo(Command command)
    {
        command.Options.Add(Provider);
        command.Options.Add(Model);
        command.Options.Add(Endpoint);
        command.Options.Add(ApiKey);
        command.Options.Add(Timeout);
        command.Options.Add(Effort);
    }

    public ProviderSettings Resolve(ParseResult parseResult, bool offline = false)
    {
        ProviderKind? kind = offline
            ? ProviderKind.None
            : parseResult.GetValue(Provider) is { } p && ProviderKinds.TryParse(p, out var parsed) ? parsed : null;

        return ProviderConfiguration.Resolve(new ProviderOverrides(
            kind,
            parseResult.GetValue(Model),
            parseResult.GetValue(Endpoint),
            parseResult.GetValue(ApiKey),
            parseResult.GetValue(Timeout),
            MaxOutputTokens: null,
            parseResult.GetValue(Effort)));
    }
}
