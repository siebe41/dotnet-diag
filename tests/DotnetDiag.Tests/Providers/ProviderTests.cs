using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;

namespace DotnetDiag.Tests.Providers;

public class ProviderTests
{
    private static readonly LlmPrompt Prompt = new("system text", "user text");

    [Fact]
    public async Task Anthropic_sends_messages_api_request_with_fallback_and_effort()
    {
        var handler = new FakeHttpHandler().Reply(HttpStatusCode.OK, """
            {"model":"claude-opus-5-5","stop_reason":"end_turn",
             "content":[{"type":"thinking","thinking":""},{"type":"text","text":"## Root cause\nMissing package."}],
             "usage":{"input_tokens":512,"output_tokens":120}}
            """);
        var provider = new AnthropicProvider(new HttpClient(handler), Settings(ProviderKind.Anthropic, "claude-opus-5-5", "https://api.anthropic.com", "sk-test"));

        var response = await provider.CompleteAsync(Prompt);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("server-side-fallback-2026-07-01", request.Headers.GetValues("anthropic-beta").Single());

        var json = JsonNode.Parse(body)!;
        Assert.Equal("claude-opus-5-5", json["model"]!.GetValue<string>());
        Assert.Equal(16000, json["max_tokens"]!.GetValue<int>());
        Assert.Equal("system text", json["system"]!.GetValue<string>());
        Assert.Equal("user", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("user text", json["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("medium", json["output_config"]!["effort"]!.GetValue<string>());
        Assert.Equal("default", json["fallbacks"]!.GetValue<string>());

        Assert.Equal("## Root cause\nMissing package.", response.Text);
        Assert.Equal(512, response.InputTokens);
        Assert.Equal(120, response.OutputTokens);
        Assert.False(response.WasTruncated);
    }

    [Fact]
    public async Task Anthropic_omits_first_party_only_features_for_gateways_and_older_models()
    {
        var handler = new FakeHttpHandler().Reply(HttpStatusCode.OK, """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""");
        var provider = new AnthropicProvider(new HttpClient(handler), Settings(ProviderKind.Anthropic, "claude-haiku-4-5", "https://llm-gateway.example.com/anthropic", "key"));

        await provider.CompleteAsync(Prompt);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://llm-gateway.example.com/anthropic/v1/messages", request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("anthropic-beta"));
        var json = JsonNode.Parse(body)!;
        Assert.Null(json["fallbacks"]);
        Assert.Null(json["output_config"]);
    }

    [Fact]
    public async Task Anthropic_refusal_becomes_a_provider_error()
    {
        var handler = new FakeHttpHandler().Reply(HttpStatusCode.OK, """{"content":[],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"}}""");
        var provider = new AnthropicProvider(new HttpClient(handler), Settings(ProviderKind.Anthropic, "claude-opus-5-5", "https://api.anthropic.com", "key"));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Prompt));
        Assert.Contains("declined", ex.Message);
        Assert.Contains("cyber", ex.Message);
    }

    [Fact]
    public async Task Http_errors_surface_the_api_error_message()
    {
        var handler = new FakeHttpHandler().Reply(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");
        var provider = new AnthropicProvider(new HttpClient(handler), Settings(ProviderKind.Anthropic, "claude-opus-5-5", "https://api.anthropic.com", "bad"));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Prompt));
        Assert.Equal("Anthropic returned HTTP 401 Unauthorized: invalid x-api-key. Check the API key.", ex.Message);
    }

    [Fact]
    public async Task Retries_transient_failures()
    {
        var handler = new FakeHttpHandler()
            .Reply(HttpStatusCode.ServiceUnavailable, "{}")
            .Reply((HttpStatusCode)529, """{"error":{"message":"overloaded"}}""")
            .Reply(HttpStatusCode.OK, """{"choices":[{"message":{"content":"fixed"},"finish_reason":"stop"}]}""");
        var delays = new List<TimeSpan>();
        var provider = new OpenAICompatibleProvider(new HttpClient(handler), Settings(ProviderKind.OpenAI, "gpt-test", "http://localhost:8000/v1", null))
        {
            Delay = (d, _) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            },
        };

        var response = await provider.CompleteAsync(Prompt);

        Assert.Equal("fixed", response.Text);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task OpenAI_compatible_uses_chat_completions_with_bearer_auth()
    {
        var handler = new FakeHttpHandler().Reply(HttpStatusCode.OK, """
            {"model":"gpt-test","choices":[{"message":{"role":"assistant","content":" Add the package. "},"finish_reason":"length"}],
             "usage":{"prompt_tokens":300,"completion_tokens":40}}
            """);
        var provider = new OpenAICompatibleProvider(new HttpClient(handler), Settings(ProviderKind.OpenAI, "gpt-test", "https://api.openai.com/v1", "sk-openai"));

        var response = await provider.CompleteAsync(Prompt);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-openai", request.Headers.Authorization.Parameter);
        var json = JsonNode.Parse(body)!;
        Assert.Equal("system", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("user text", json["messages"]![1]!["content"]!.GetValue<string>());
        Assert.Null(json["max_tokens"]);

        Assert.Equal("Add the package.", response.Text);
        Assert.Equal(300, response.InputTokens);
        Assert.True(response.WasTruncated);
    }

    [Fact]
    public async Task Ollama_uses_the_local_chat_api_without_streaming()
    {
        var handler = new FakeHttpHandler().Reply(HttpStatusCode.OK, """
            {"model":"llama3.1","message":{"role":"assistant","content":"Run dotnet restore."},"done":true,"done_reason":"stop","prompt_eval_count":400,"eval_count":25}
            """);
        var provider = new OllamaProvider(new HttpClient(handler), Settings(ProviderKind.Ollama, "llama3.1", "http://localhost:11434", null));

        var response = await provider.CompleteAsync(Prompt);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/api/chat", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        var json = JsonNode.Parse(body)!;
        Assert.False(json["stream"]!.GetValue<bool>());
        Assert.Equal("llama3.1", json["model"]!.GetValue<string>());
        Assert.Equal("Run dotnet restore.", response.Text);
        Assert.Equal(25, response.OutputTokens);
    }

    [Fact]
    public async Task Ollama_errors_explain_how_to_fix_them()
    {
        var settings = Settings(ProviderKind.Ollama, "qwen2.5-coder", "http://localhost:11434", null);
        var notFound = new OllamaProvider(new HttpClient(new FakeHttpHandler().Reply(HttpStatusCode.NotFound, """{"error":"model 'qwen2.5-coder' not found"}""")), settings);
        var refused = new OllamaProvider(
            new HttpClient(new FakeHttpHandler().Throw(new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused)))),
            settings);

        Assert.Contains("ollama pull qwen2.5-coder", (await Assert.ThrowsAsync<LlmProviderException>(() => notFound.CompleteAsync(Prompt))).Message);
        Assert.Contains("ollama serve", (await Assert.ThrowsAsync<LlmProviderException>(() => refused.CompleteAsync(Prompt))).Message);
    }

    [Fact]
    public void Factory_returns_null_when_no_usable_provider_is_configured()
    {
        using var http = new HttpClient();

        Assert.Null(ProviderFactory.Create(new ProviderSettings(), http));
        Assert.Null(ProviderFactory.Create(new ProviderSettings { Kind = ProviderKind.Anthropic, Problem = "no key" }, http));
        Assert.IsType<AnthropicProvider>(ProviderFactory.Create(Settings(ProviderKind.Anthropic, "m", "https://api.anthropic.com", "k"), http));
        Assert.IsType<OllamaProvider>(ProviderFactory.Create(Settings(ProviderKind.Ollama, "m", "http://localhost:11434", null), http));
        Assert.IsType<OpenAICompatibleProvider>(ProviderFactory.Create(Settings(ProviderKind.OpenAI, "m", "http://localhost:1234/v1", null), http));
    }

    private static ProviderSettings Settings(ProviderKind kind, string model, string endpoint, string? apiKey) => new()
    {
        Kind = kind,
        Model = model,
        Endpoint = new Uri(endpoint),
        ApiKey = apiKey,
        Timeout = TimeSpan.FromSeconds(10),
    };
}
