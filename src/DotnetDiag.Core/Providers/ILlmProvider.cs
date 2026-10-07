using DotnetDiag.Core.Context;

namespace DotnetDiag.Core.Providers;

public interface ILlmProvider
{
    ProviderKind Kind { get; }

    string Model { get; }

    Task<LlmResponse> CompleteAsync(LlmPrompt prompt, CancellationToken cancellationToken = default);
}

public sealed record LlmResponse(string Text, string Model, int? InputTokens = null, int? OutputTokens = null, string? StopReason = null)
{
    public bool WasTruncated => StopReason is "max_tokens" or "length";
}

public sealed class LlmProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
