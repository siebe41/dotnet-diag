using System.Net.Http.Headers;
using DotnetDiag.Core;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Gui.Services;

/// <summary>Everything one analysis needs: the desktop counterpart of the <c>analyze</c> command line.</summary>
public sealed record AnalysisRequest
{
    /// <summary>A log file, or a folder whose newest binlog is used.</summary>
    public string? InputPath { get; init; }

    /// <summary>Log text pasted into the app: the counterpart of piping into stdin. Wins over <see cref="InputPath"/>.</summary>
    public string? PastedText { get; init; }

    /// <summary>Source tree the log refers to (<c>--repo</c>).</summary>
    public string? Repository { get; init; }

    /// <summary>Fallback source tree from the settings, used when the log is not inside a repository itself.</summary>
    public string? DefaultRepository { get; init; }

    /// <summary>Never call a model (<c>--offline</c>).</summary>
    public bool Offline { get; init; }

    public ProviderOverrides Overrides { get; init; } = new();

    public ContextBuilderOptions Context { get; init; } = new();
}

/// <summary>A finished run. <see cref="Error"/> is set instead of <see cref="Result"/> when the input was unusable.</summary>
public sealed record AnalysisOutcome(DiagnosisResult? Result, ProviderSettings Settings, string? Error);

/// <summary>Runs one analysis for the desktop app: the counterpart of the CLI's <c>analyze</c> command.</summary>
public static class AnalysisService
{
    public const string PastedSourceName = "pasted log";

    public static ProviderSettings ResolveProvider(AnalysisRequest request) =>
        ProviderConfiguration.Resolve(request.Offline ? request.Overrides with { Kind = ProviderKind.None } : request.Overrides);

    public static async Task<AnalysisOutcome> RunAsync(
        AnalysisRequest request,
        IProgress<PipelineStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = ResolveProvider(request);

        if (request.Context.MaxTokens is < ContextBuilderOptions.MinTokenBudget or > ContextBuilderOptions.MaxTokenBudget)
        {
            return new AnalysisOutcome(null, settings,
                $"The token budget must be between {ContextBuilderOptions.MinTokenBudget} and {ContextBuilderOptions.MaxTokenBudget}.");
        }

        // Everything from here touches the disk (a folder scan, the walk up to the repository root, parsing a
        // large binlog), so none of it may run on the UI thread.
        var work = Task.Run(
            async () =>
            {
                var source = ResolveInput(request);
                if (source.Error is not null)
                {
                    return new AnalysisOutcome(null, settings, source.Error);
                }

                if (!string.IsNullOrWhiteSpace(request.Repository) && !Directory.Exists(request.Repository))
                {
                    return new AnalysisOutcome(null, settings, $"Repository folder not found: {request.Repository}");
                }

                var workspace = ResolveWorkspace(request, source.Path);
                using var http = CreateHttpClient();
                var pipeline = new DiagnosisPipeline(ProviderFactory.Create(settings, http), request.Context);
                try
                {
                    var result = source.Path is not null
                        ? await pipeline.AnalyzeFileAsync(source.Path, workspace, progress, cancellationToken)
                        : await pipeline.AnalyzeTextAsync(source.Text!, source.Name, workspace, progress, cancellationToken);
                    return new AnalysisOutcome(result, settings, null);
                }
                catch (LogParseException ex)
                {
                    return new AnalysisOutcome(null, settings, ex.Message);
                }
            },
            cancellationToken);

        // Parsing cannot be interrupted, but the caller does not have to wait for it: Cancel returns at once and
        // the abandoned work finishes in the background, where its result is dropped.
        return await work.WaitAsync(cancellationToken);
    }

    internal sealed record InputSource(string Name, string? Path, string? Text, string? Error);

    internal static InputSource ResolveInput(AnalysisRequest request)
    {
        if (request.PastedText is { } text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? new InputSource(PastedSourceName, null, null, "The pasted text is empty.")
                : new InputSource(PastedSourceName, null, text, null);
        }

        // Paths copied from Explorer or Finder often arrive wrapped in quotes.
        var input = request.InputPath?.Trim().Trim('"');
        if (string.IsNullOrEmpty(input))
        {
            return new InputSource(string.Empty, null, null, "No input. Open a log file or a folder, drop one on the window, or paste build output.");
        }

        if (Directory.Exists(input))
        {
            var latest = LogParser.FindLatestBinaryLog(input);
            return latest is null
                ? new InputSource(input, null, null, $"No .binlog files found in '{input}'.")
                : new InputSource(Path.GetFileName(latest), latest, null, null);
        }

        return File.Exists(input)
            ? new InputSource(Path.GetFileName(input), Path.GetFullPath(input), null, null)
            : new InputSource(input, null, null, $"File not found: {input}");
    }

    /// <summary>
    /// The CLI defaults to the repository around the current directory. A desktop app has no meaningful current
    /// directory, so the repository around the log itself comes first, then the default from the settings.
    /// </summary>
    internal static GitWorkspace? ResolveWorkspace(AnalysisRequest request, string? inputPath)
    {
        if (!string.IsNullOrWhiteSpace(request.Repository))
        {
            return GitWorkspace.ForDirectory(request.Repository);
        }

        if (inputPath is not null && GitWorkspace.Discover(Path.GetDirectoryName(inputPath)!) is { } aroundInput)
        {
            return aroundInput;
        }

        return !string.IsNullOrWhiteSpace(request.DefaultRepository) && Directory.Exists(request.DefaultRepository)
            ? GitWorkspace.ForDirectory(request.DefaultRepository)
            : GitWorkspace.Discover(Environment.CurrentDirectory);
    }

    private static HttpClient CreateHttpClient()
    {
        // Per-request timeouts are enforced by the providers.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var version = typeof(AnalysisService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dotnet-diag-gui", version));
        return http;
    }
}
