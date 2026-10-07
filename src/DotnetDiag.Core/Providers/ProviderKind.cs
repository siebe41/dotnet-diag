namespace DotnetDiag.Core.Providers;

public enum ProviderKind
{
    None,
    Ollama,
    Anthropic,
    OpenAI,
}

public static class ProviderKinds
{
    public static bool TryParse(string? value, out ProviderKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "none" or "off" or "offline" or "local":
                kind = ProviderKind.None;
                return true;
            case "ollama":
                kind = ProviderKind.Ollama;
                return true;
            case "anthropic" or "claude":
                kind = ProviderKind.Anthropic;
                return true;
            case "openai" or "openai-compatible" or "openai_compatible" or "oai":
                kind = ProviderKind.OpenAI;
                return true;
            default:
                kind = ProviderKind.None;
                return false;
        }
    }

    public static string DisplayName(this ProviderKind kind) => kind switch
    {
        ProviderKind.Ollama => "Ollama",
        ProviderKind.Anthropic => "Anthropic",
        ProviderKind.OpenAI => "OpenAI-compatible",
        _ => "none",
    };
}
