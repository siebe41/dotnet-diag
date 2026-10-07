using System.Text;
using System.Text.Json.Nodes;
using DotnetDiag.Core.Context;

namespace DotnetDiag.Core.Providers;

/// <summary>Anthropic Messages API (<c>POST /v1/messages</c>) over raw HTTP.</summary>
public sealed class AnthropicProvider(HttpClient httpClient, ProviderSettings settings) : HttpLlmProvider(httpClient, settings)
{
    public const string ApiVersion = "2023-06-01";
    public const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <summary>Thinking counts toward max_tokens, so leave generous room for a short answer.</summary>
    public const int DefaultMaxTokens = 16000;

    /// <summary>Models that accept output_config.effort and server-side refusal fallbacks.</summary>
    private static readonly HashSet<string> CurrentGenerationModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude-opus-5-5", "claude-opus-5", "claude-fable-5-1", "claude-sonnet-5-5",
    };

    public override ProviderKind Kind => ProviderKind.Anthropic;

    private bool IsCurrentGeneration => CurrentGenerationModels.Contains(Model);

    /// <summary>Server-side fallbacks exist on the first-party API only.</summary>
    private bool UseServerSideFallback =>
        IsCurrentGeneration && Endpoint.Host.Equals("api.anthropic.com", StringComparison.OrdinalIgnoreCase);

    protected override HttpRequestMessage CreateRequest(LlmPrompt prompt)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = Settings.MaxOutputTokens ?? DefaultMaxTokens,
            ["system"] = prompt.System,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = prompt.User },
            },
        };

        var effort = Settings.Effort ?? (IsCurrentGeneration ? "medium" : null);
        if (effort is not null)
        {
            body["output_config"] = new JsonObject { ["effort"] = effort };
        }

        if (UseServerSideFallback)
        {
            // Re-run a request declined by a safety classifier on Anthropic's recommended fallback model.
            body["fallbacks"] = "default";
        }

        var request = JsonPost(Combine(Endpoint, "v1/messages"), body);
        request.Headers.Add("x-api-key", Settings.ApiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        if (UseServerSideFallback)
        {
            request.Headers.Add("anthropic-beta", FallbackBeta);
        }

        return request;
    }

    protected override LlmResponse ParseResponse(JsonNode json)
    {
        var stopReason = json["stop_reason"]?.GetValue<string>();
        if (stopReason == "refusal")
        {
            var category = json["stop_details"]?["category"]?.GetValue<string>();
            throw new LlmProviderException(
                $"The model declined to answer{(category is null ? string.Empty : $" (category: {category})")}. Showing local hints only.");
        }

        var text = new StringBuilder();
        if (json["content"] is JsonArray content)
        {
            foreach (var block in content)
            {
                if (block?["type"]?.GetValue<string>() == "text")
                {
                    text.Append(block["text"]?.GetValue<string>());
                }
            }
        }

        return new LlmResponse(
            text.ToString().Trim(),
            json["model"]?.GetValue<string>() ?? Model,
            json["usage"]?["input_tokens"]?.GetValue<int>(),
            json["usage"]?["output_tokens"]?.GetValue<int>(),
            stopReason);
    }
}
