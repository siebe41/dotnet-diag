using System.Globalization;
using System.Text;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;
using DotnetDiag.Eval;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;
if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var columns) && columns >= 40)
{
    AnsiConsole.Profile.Width = columns;
}
else if (Console.IsOutputRedirected || AnsiConsole.Profile.Width < 40)
{
    AnsiConsole.Profile.Width = 120;
}

var options = EvalOptions.Parse(args);
if (options is null)
{
    Console.WriteLine(EvalOptions.Usage);
    return 2;
}

if (options.ReviewDirectory is { } reviewDirectory)
{
    var reviewSettings = ProviderConfiguration.Resolve(options.Provider);
    using var reviewHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    return await FieldTrial.RunAsync(
        reviewDirectory,
        options.Repo,
        ProviderFactory.Create(reviewSettings, reviewHttp),
        new ContextBuilderOptions { MaxTokens = options.MaxContextTokens },
        options.ReportPath ?? Path.Combine("artifacts", "eval", "field-trial.md"));
}

var scenariosRoot = options.ScenariosDirectory ?? FindScenariosDirectory();
if (scenariosRoot is null || !Directory.Exists(scenariosRoot))
{
    AnsiConsole.MarkupLine("[red]error:[/] could not find eval/scenarios; pass --scenarios <dir>.");
    return 2;
}

var scenarios = Directory.GetDirectories(scenariosRoot)
    .Where(d => File.Exists(Path.Combine(d, "scenario.json")))
    .Where(d => options.Filter is null || Path.GetFileName(d).Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
    .Order(StringComparer.Ordinal)
    .Select(Scenario.Load)
    .ToList();
if (scenarios.Count == 0)
{
    AnsiConsole.MarkupLine("[red]error:[/] no scenarios matched.");
    return 2;
}

if (options.SamplesDirectory is { } samplesDirectory)
{
    return await Samples.GenerateAsync(scenarios, samplesDirectory, new ScenarioRunner(null, new ContextBuilderOptions(), keepWorkDirectories: false));
}

var settings = ProviderConfiguration.Resolve(options.Provider);
if (settings.Kind != ProviderKind.None && settings.Problem is not null)
{
    AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(settings.Problem)}");
    return 2;
}

using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var provider = ProviderFactory.Create(settings, http);
var runner = new ScenarioRunner(provider, new ContextBuilderOptions { MaxTokens = options.MaxContextTokens }, options.Keep);

AnsiConsole.Write(new Rule($"[bold deepskyblue1]dotnet-diag eval[/] [grey]· {scenarios.Count} scenarios · {Markup.Escape(Environment.OSVersion.ToString())} · AI: {Markup.Escape(provider is null ? "off" : $"{settings.Kind.DisplayName()} {settings.Model}")}[/]") { Justification = Justify.Left });

var results = new List<ScenarioResult>();
foreach (var scenario in scenarios)
{
    ScenarioResult result = null!;
    if (AnsiConsole.Profile.Capabilities.Interactive)
    {
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(
            $"[bold]{Markup.Escape(scenario.Name)}[/]: breaking the {Markup.Escape(scenario.Command)}, diagnosing, verifying the fix…",
            async _ => result = await runner.RunAsync(scenario, CancellationToken.None));
    }
    else
    {
        result = await runner.RunAsync(scenario, CancellationToken.None);
    }

    results.Add(result);

    var badge = result.Passed ? "[green]PASS[/]" : scenario.KnownGap is null ? "[red]FAIL[/]" : "[yellow]GAP [/]";
    AnsiConsole.MarkupLine($"{badge} [bold]{Markup.Escape(scenario.Name)}[/] [grey]({result.Elapsed.TotalSeconds:0.0}s)[/]");
    var failures = result.Inputs.SelectMany(i => i.Checks.Select(c => (Input: (string?)i.Input, Check: c))).Append((null, result.FixCheck));
    foreach (var (input, check) in failures.Where(f => f.Check.Passed == false))
    {
        AnsiConsole.MarkupLine($"     [red]✖[/] {Markup.Escape(check.Name)}{(input is null ? "" : $" [grey][[{Markup.Escape(input)}]][/]")}: {Markup.Escape(check.Detail)}");
    }

    if (options.Keep)
    {
        AnsiConsole.MarkupLine($"     [grey]kept: {Markup.Escape(result.WorkDirectory)}[/]");
    }
}

var scorecard = Scorecard.From(results);
AnsiConsole.WriteLine();
AnsiConsole.Write(Report.SummaryTable(results));
AnsiConsole.Write(Report.ScorecardTable(scorecard));

if (options.ReportPath is { } reportPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    File.WriteAllText(reportPath, Report.Markdown(results, scorecard, settings, provider is not null));
    AnsiConsole.MarkupLine($"[grey]Report written to {Markup.Escape(reportPath)}[/]");
}

var unexpectedFailures = results.Where(r => !r.Passed && (r.Scenario.KnownGap is null || options.Strict)).ToList();
var closedGaps = results.Where(r => r.Passed && r.Scenario.KnownGap is not null).ToList();
foreach (var closed in closedGaps)
{
    AnsiConsole.MarkupLine($"[green]★[/] Known gap [bold]{Markup.Escape(closed.Scenario.Name)}[/] now passes — remove \"knownGap\" from its scenario.json.");
}

return unexpectedFailures.Count == 0 ? 0 : 1;

static string? FindScenariosDirectory()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "eval", "scenarios");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}

internal sealed record EvalOptions(
    string? SamplesDirectory,
    string? ReviewDirectory,
    string? Repo,
    string? ScenariosDirectory,
    string? Filter,
    string? ReportPath,
    bool Keep,
    bool Strict,
    int MaxContextTokens,
    ProviderOverrides Provider)
{
    public const string Usage = """
        dotnet run --project eval/DotnetDiag.Eval -- [options]

          --filter <text>             Only scenarios whose folder name contains <text>
          --scenarios <dir>           Scenario folder (default: eval/scenarios)
          --report <file.md>          Write a Markdown report (scorecard, per-scenario checks, AI answers)
          --keep                      Keep the temp copies of broken projects for inspection
          --strict                    Known-gap scenarios fail the run too
          --max-context-tokens <n>    Budget for the LLM bundle (default 1000)
          --provider <name>           none | ollama | anthropic | openai  (default: offline)

        Sample logs:
          --generate-samples <dir>    Build every (filtered) scenario once and save <name>.binlog + <name>.txt to <dir>

        Field trial (real logs, graded by people):
          --review <dir>              Analyze every .binlog/.log/.txt in <dir> and write a review sheet (.md + .csv)
          --repo <dir>                Source checkout the logs belong to (for paths and source context)
          --model <id> --endpoint <url> --effort <level>
        """;

    public static EvalOptions? Parse(string[] args)
    {
        string? samples = null, review = null, repo = null, scenarios = null, filter = null, report = null, model = null, endpoint = null, effort = null;
        ProviderKind kind = ProviderKind.None;
        bool keep = false, strict = false;
        var budget = 1000;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--generate-samples": samples = Next(); break;
                case "--review": review = Next(); break;
                case "--repo": repo = Next(); break;
                case "--scenarios": scenarios = Next(); break;
                case "--filter": filter = Next(); break;
                case "--report": report = Next(); break;
                case "--keep": keep = true; break;
                case "--strict": strict = true; break;
                case "--max-context-tokens": budget = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--provider":
                    if (!ProviderKinds.TryParse(Next(), out kind))
                    {
                        return null;
                    }

                    break;
                case "--model": model = Next(); break;
                case "--endpoint": endpoint = Next(); break;
                case "--effort": effort = Next(); break;
                default: return null;
            }
        }

        // Offline unless a provider is explicitly requested on the command line.
        return new EvalOptions(samples, review, repo, scenarios, filter, report, keep, strict, budget,
            new ProviderOverrides(kind, model, endpoint, Effort: effort));
    }
}
