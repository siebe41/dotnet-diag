using System.CommandLine;
using System.Net.Http.Headers;
using DotnetDiag.Cli.Rendering;
using DotnetDiag.Core;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Reporting;
using DotnetDiag.Core.Workspace;
using Spectre.Console;

namespace DotnetDiag.Cli.Commands;

internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var input = new Argument<string?>("input")
        {
            Description = "A .binlog, a text build/test log, or '-' for stdin. Omit when piping: `dotnet test | dotnet-diag analyze`.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var providers = new ProviderOptionSet();
        var offline = new Option<bool>("--offline")
        {
            Description = "Never call an AI provider; print local diagnostic hints only.",
        };
        var repo = new Option<DirectoryInfo?>("--repo")
        {
            Description = "Root of the source tree the log refers to (default: the git repository containing the current directory).",
            HelpName = "dir",
        };
        var maxContextTokens = new Option<int>("--max-context-tokens")
        {
            Description = "Token budget for the diagnostic bundle sent to the model.",
            DefaultValueFactory = _ => 1000,
            HelpName = "tokens",
        };
        var format = new Option<OutputFormat>("--format", "-f")
        {
            Description = "Output format: pretty (rich terminal), markdown (e.g. for $GITHUB_STEP_SUMMARY) or json.",
            DefaultValueFactory = _ => OutputFormat.Pretty,
        };
        var showContext = new Option<bool>("--show-context")
        {
            Description = "Print the exact context bundle that is (or would be) sent to the model.",
        };
        var warnings = new Option<bool>("--warnings")
        {
            Description = "List warnings in the diagnostics table, not just a summary.",
        };
        var exitCode = new Option<bool>("--exit-code")
        {
            Description = "Exit with code 1 when errors or failing tests are found (useful as a CI gate).",
        };

        var command = new Command("analyze", "Analyze a build log, binary log or dotnet test output and explain the failure.")
        {
            input,
            offline,
            repo,
            maxContextTokens,
            format,
            showContext,
            warnings,
            exitCode,
        };
        providers.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var outputFormat = parseResult.GetValue(format);
            var console = AnsiConsole.Console;
            var errorConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });
            ConsoleSetup.Configure(errorConsole);

            var budget = parseResult.GetValue(maxContextTokens);
            if (budget is < ContextBuilderOptions.MinTokenBudget or > ContextBuilderOptions.MaxTokenBudget)
            {
                errorConsole.MarkupLine($"[red]error:[/] --max-context-tokens must be between {ContextBuilderOptions.MinTokenBudget} and {ContextBuilderOptions.MaxTokenBudget}.");
                return ExitCodes.InvalidInput;
            }

            // 1. Input
            var source = await ReadInputAsync(parseResult.GetValue(input), cancellationToken);
            if (source.Error is not null)
            {
                errorConsole.MarkupLine($"[red]error:[/] {Markup.Escape(source.Error)}");
                return ExitCodes.InvalidInput;
            }

            // 2. Workspace and provider
            var workspace = ResolveWorkspace(parseResult.GetValue(repo), source.Path);
            var settings = providers.Resolve(parseResult, parseResult.GetValue(offline));
            if (settings.Kind != ProviderKind.None && settings.Problem is not null)
            {
                errorConsole.MarkupLine($"[yellow]warning:[/] {Markup.Escape(settings.Problem)} Continuing with local hints only.");
            }

            using var http = CreateHttpClient();
            var provider = ProviderFactory.Create(settings, http);
            var pipeline = new DiagnosisPipeline(provider, new ContextBuilderOptions { MaxTokens = budget });

            Task<DiagnosisResult> Run(IProgress<PipelineStage>? progress) => source.Path is not null
                ? pipeline.AnalyzeFileAsync(source.Path, workspace, progress, cancellationToken)
                : pipeline.AnalyzeTextAsync(source.Text!, source.Name, workspace, progress, cancellationToken);

            // 3. Pipeline
            DiagnosisResult result;
            try
            {
                if (outputFormat == OutputFormat.Pretty && console.Profile.Capabilities.Interactive)
                {
                    result = await console.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(Theme.Accent)
                        .StartAsync($"Parsing {Markup.Escape(source.Name)}…", ctx =>
                            Run(new InlineProgress<PipelineStage>(stage => ctx.Status(StatusText(stage, source.Name, settings, budget)))));
                }
                else
                {
                    result = await Run(null);
                }
            }
            catch (LogParseException ex)
            {
                errorConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
                return ExitCodes.InvalidInput;
            }

            // 4. Output
            switch (outputFormat)
            {
                case OutputFormat.Json:
                    Console.Out.WriteLine(JsonReportWriter.Write(result));
                    break;
                case OutputFormat.Markdown:
                    Console.Out.Write(MarkdownReportWriter.Write(result));
                    break;
                default:
                    ConsoleReportRenderer.Render(console, result, new RenderOptions(
                        ShowWarnings: parseResult.GetValue(warnings),
                        ShowContext: parseResult.GetValue(showContext),
                        ProviderProblem: settings.Kind != ProviderKind.None ? settings.Problem : null));
                    break;
            }

            if (outputFormat != OutputFormat.Pretty && parseResult.GetValue(showContext))
            {
                Console.Error.WriteLine(result.Prompt.User);
            }

            return parseResult.GetValue(exitCode) && result.Log.HasProblems ? ExitCodes.ProblemsFound : ExitCodes.Success;
        });

        return command;
    }

    private static string StatusText(PipelineStage stage, string sourceName, ProviderSettings settings, int budget) => stage switch
    {
        PipelineStage.Parsing => $"Parsing [bold]{Markup.Escape(sourceName)}[/]…",
        PipelineStage.InspectingWorkspace => "Inspecting git working tree…",
        PipelineStage.Analyzing => "Matching known MSBuild, NuGet and compiler patterns…",
        PipelineStage.BuildingContext => $"Building a {budget}-token diagnostic bundle…",
        PipelineStage.QueryingModel => $"Asking [bold]{Markup.Escape(settings.Kind.DisplayName())}[/] ({Markup.Escape(settings.Model ?? "?")}) for a diagnosis…",
        _ => "Working…",
    };

    private sealed record InputSource(string Name, string? Path, string? Text, string? Error);

    private static async Task<InputSource> ReadInputAsync(string? input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input) || input == "-")
        {
            if (!Console.IsInputRedirected)
            {
                return new InputSource("stdin", null, null,
                    "No input. Pass a log file (dotnet-diag analyze msbuild.binlog) or pipe output (dotnet test 2>&1 | dotnet-diag analyze).");
            }

            // Raw bytes: Console.In would decode with the OEM code page on Windows.
            using var stdin = Console.OpenStandardInput();
            var text = await TextDecoding.ReadStreamAsync(stdin, cancellationToken);
            return string.IsNullOrWhiteSpace(text)
                ? new InputSource("stdin", null, null, "stdin was empty.")
                : new InputSource("stdin", null, text, null);
        }

        if (Directory.Exists(input))
        {
            // A directory: use the most recent binary log in it (dotnet build -bl writes msbuild.binlog).
            var latest = LogParser.FindLatestBinaryLog(input);
            return latest is null
                ? new InputSource(input, null, null, $"No .binlog files found in '{input}'.")
                : new InputSource(Path.GetFileName(latest), latest, null, null);
        }

        return File.Exists(input)
            ? new InputSource(Path.GetFileName(input), Path.GetFullPath(input), null, null)
            : new InputSource(input, null, null, $"File not found: {input}");
    }

    private static GitWorkspace? ResolveWorkspace(DirectoryInfo? repo, string? inputPath)
    {
        if (repo is not null)
        {
            return GitWorkspace.ForDirectory(repo.FullName);
        }

        return GitWorkspace.Discover(Environment.CurrentDirectory)
            ?? (inputPath is null ? null : GitWorkspace.Discover(Path.GetDirectoryName(inputPath)!));
    }

    private static HttpClient CreateHttpClient()
    {
        // Per-request timeouts are enforced by the providers.
        var http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var version = typeof(AnalyzeCommand).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dotnet-diag", version));
        return http;
    }

    /// <summary>Synchronous IProgress so spinner updates happen in order on the calling thread.</summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
