using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetDiag.Core.Context;

namespace DotnetDiag.Core.Providers;

/// <summary>Shared HTTP plumbing: JSON bodies, per-request timeout, retries for 429/5xx, readable errors.</summary>
public abstract class HttpLlmProvider(HttpClient httpClient, ProviderSettings settings) : ILlmProvider
{
    private const int MaxAttempts = 3;

    public abstract ProviderKind Kind { get; }

    public string Model => Settings.Model ?? string.Empty;

    protected ProviderSettings Settings { get; } = settings;

    protected Uri Endpoint => Settings.Endpoint ?? throw new LlmProviderException($"{Kind.DisplayName()} endpoint is not configured.");

    /// <summary>Test hook: replaces Task.Delay between retries.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public async Task<LlmResponse> CompleteAsync(LlmPrompt prompt, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = CreateRequest(prompt);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Settings.Timeout);

            HttpResponseMessage response;
            string body;
            try
            {
                response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LlmProviderException($"{Kind.DisplayName()} did not answer within {Settings.Timeout.TotalSeconds:0}s (raise it with --timeout).");
            }
            catch (HttpRequestException ex)
            {
                throw new LlmProviderException(DescribeConnectionFailure(ex), ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    JsonNode? json;
                    try
                    {
                        json = JsonNode.Parse(body);
                    }
                    catch (JsonException ex)
                    {
                        throw new LlmProviderException($"{Kind.DisplayName()} returned a response that is not JSON.", ex);
                    }

                    return ParseResponse(json ?? throw new LlmProviderException($"{Kind.DisplayName()} returned an empty response."));
                }

                if (IsRetryable(response.StatusCode) && attempt < MaxAttempts)
                {
                    await Delay(RetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new LlmProviderException(DescribeHttpError(response.StatusCode, body));
            }
        }
    }

    protected abstract HttpRequestMessage CreateRequest(LlmPrompt prompt);

    protected abstract LlmResponse ParseResponse(JsonNode json);

    protected virtual string DescribeConnectionFailure(HttpRequestException ex)
    {
        var refused = ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
        return refused
            ? $"Could not connect to {Endpoint} (connection refused)."
            : $"Could not reach {Kind.DisplayName()} at {Endpoint}: {ex.Message}";
    }

    protected virtual string DescribeHttpError(HttpStatusCode status, string body)
    {
        var detail = ExtractErrorMessage(body);
        var hint = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => " Check the API key.",
            HttpStatusCode.NotFound => " Check the endpoint and model name.",
            HttpStatusCode.TooManyRequests => " Rate limited; try again later.",
            _ => string.Empty,
        };
        return $"{Kind.DisplayName()} returned HTTP {(int)status} {status}: {detail}.{hint}";
    }

    protected static HttpRequestMessage JsonPost(Uri uri, JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    protected static Uri Combine(Uri baseUri, string relative) =>
        new(baseUri.AbsoluteUri.TrimEnd('/') + "/" + relative.TrimStart('/'));

    internal static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(empty body)";
        }

        try
        {
            var json = JsonNode.Parse(body);
            var error = json?["error"];
            var message = error switch
            {
                JsonObject o => o["message"]?.GetValue<string>(),
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => json?["message"]?.GetValue<string>(),
            };
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
        }

        return body.Length > 300 ? body[..300] + "…" : body;
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout || (int)status == 529;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta <= TimeSpan.FromSeconds(30))
        {
            return delta;
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
    }
}
