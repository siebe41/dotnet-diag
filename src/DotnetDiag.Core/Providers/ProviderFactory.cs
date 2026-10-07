namespace DotnetDiag.Core.Providers;

public static class ProviderFactory
{
    /// <summary>Creates the configured provider, or null when no (usable) provider is configured.</summary>
    public static ILlmProvider? Create(ProviderSettings settings, HttpClient httpClient)
    {
        if (!settings.IsEnabled)
        {
            return null;
        }

        return settings.Kind switch
        {
            ProviderKind.Anthropic => new AnthropicProvider(httpClient, settings),
            ProviderKind.OpenAI => new OpenAICompatibleProvider(httpClient, settings),
            ProviderKind.Ollama => new OllamaProvider(httpClient, settings),
            _ => null,
        };
    }
}
