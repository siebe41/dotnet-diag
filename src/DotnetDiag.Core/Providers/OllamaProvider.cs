using System.Net;
using System.Text.Json.Nodes;
using DotnetDiag.Core.Context;

namespace DotnetDiag.Core.Providers;

/// <summary>
/// Local models through the Ollama daemon (the server behind the <c>ollama</c> CLI), via <c>POST /api/chat</c>.
/// Nothing leaves the machine.
/// </summary>
public sealed class OllamaProvider(HttpClient httpClient, ProviderSettings settings) : HttpLlmProvider(httpClient, settings)
{
    public override ProviderKind Kind => ProviderKind.Ollama;

    protected override HttpRequestMessage CreateRequest(LlmPrompt prompt)
    {
        var options = new JsonObject { ["temperature"] = 0.2 };
        if (Settings.MaxOutputTokens is { } max)
        {
            options["num_predict"] = max;
        }

        var body = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = prompt.System },
                new JsonObject { ["role"] = "user", ["content"] = prompt.User },
            },
            ["options"] = options,
        };

        return JsonPost(Combine(Endpoint, "api/chat"), body);
    }

    protected override LlmResponse ParseResponse(JsonNode json)
    {
        var text = json["message"]?["content"]?.GetValue<string>()
            ?? throw new LlmProviderException("Ollama returned no message content.");

        return new LlmResponse(
            text.Trim(),
            json["model"]?.GetValue<string>() ?? Model,
            json["prompt_eval_count"]?.GetValue<int>(),
            json["eval_count"]?.GetValue<int>(),
            json["done_reason"]?.GetValue<string>());
    }

    protected override string DescribeConnectionFailure(HttpRequestException ex) =>
        $"{base.DescribeConnectionFailure(ex)} Is Ollama running? Start it with `ollama serve` and pull the model with `ollama pull {Model}`.";

    protected override string DescribeHttpError(HttpStatusCode status, string body) =>
        status == HttpStatusCode.NotFound
            ? $"Ollama does not have the model '{Model}'. Pull it with `ollama pull {Model}` or choose another with --model."
            : base.DescribeHttpError(status, body);
}
