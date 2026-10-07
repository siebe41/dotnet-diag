using System.Text.Json.Nodes;
using DotnetDiag.Core.Context;

namespace DotnetDiag.Core.Providers;

/// <summary>
/// Any server implementing <c>POST {endpoint}/chat/completions</c>: OpenAI, Azure OpenAI (v1 endpoint),
/// vLLM, LM Studio, llama.cpp server, LiteLLM, OpenRouter, etc.
/// </summary>
public sealed class OpenAICompatibleProvider(HttpClient httpClient, ProviderSettings settings) : HttpLlmProvider(httpClient, settings)
{
    public override ProviderKind Kind => ProviderKind.OpenAI;

    protected override HttpRequestMessage CreateRequest(LlmPrompt prompt)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = prompt.System },
                new JsonObject { ["role"] = "user", ["content"] = prompt.User },
            },
        };

        if (Settings.MaxOutputTokens is { } max)
        {
            body["max_tokens"] = max;
        }

        var request = JsonPost(Combine(Endpoint, "chat/completions"), body);
        if (!string.IsNullOrEmpty(Settings.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Settings.ApiKey);
        }

        return request;
    }

    protected override LlmResponse ParseResponse(JsonNode json)
    {
        var choice = json["choices"]?[0];
        var text = choice?["message"]?["content"]?.GetValue<string>()
            ?? throw new LlmProviderException("The OpenAI-compatible endpoint returned no message content.");

        return new LlmResponse(
            text.Trim(),
            json["model"]?.GetValue<string>() ?? Model,
            json["usage"]?["prompt_tokens"]?.GetValue<int>(),
            json["usage"]?["completion_tokens"]?.GetValue<int>(),
            choice?["finish_reason"]?.GetValue<string>());
    }
}
