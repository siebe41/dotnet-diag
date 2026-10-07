using System.Globalization;
using DotnetDiag.Core;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Reporting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetDiag.Cli.Rendering;

internal sealed record RenderOptions(bool ShowWarnings = false, bool ShowContext = false, string? ProviderProblem = null);

/// <summary>Rich terminal report: summary, color-coded diagnostics table, tests, packages, diagnosis.</summary>
internal static class ConsoleReportRenderer
{
    private const int MaxTableRows = 15;

    public static void Render(IAnsiConsole console, DiagnosisResult result, RenderOptions options)
    {
        var log = result.Log;
        string Rel(string p) => result.Workspace?.ToRelative(p) ?? p.Replace('\\', '/');

        console.Write(new Rule($"[bold deepskyblue1]dotnet-diag[/] [grey]›[/] {Markup.Escape(log.SourceName)}") { Justification = Justify.Left, Style = Theme.Border });
        console.WriteLine();
        console.Write(Summary(result));
        console.WriteLine();

        var diagnosticsTable = DiagnosticsTable(log, options.ShowWarnings, Rel, text => result.Workspace?.RelativizeText(text) ?? text);
        if (diagnosticsTable is not null)
        {
            console.Write(diagnosticsTable);
        }

        if (!options.ShowWarnings && log.WarningCount > 0)
        {
            var top = log.Warnings.GroupBy(w => w.Code).OrderByDescending(g => g.Count()).Take(4)
                .Select(g => $"{Markup.Escape(g.Key)} ×{g.Count()}");
            console.MarkupLine($"[yellow]▲[/] [grey]{log.WarningCount} warning(s): {string.Join(", ", top)} — use [bold]--warnings[/] to list them.[/]");
        }

        if (log.TestFailures.Count > 0)
        {
            console.WriteLine();
            console.Write(TestTable(log, Rel));
        }

        if (result.Analysis.MissingPackages.Count > 0)
        {
            console.WriteLine();
            console.Write(MissingPackagesTable(result.Analysis.MissingPackages));
        }

        if (result.Analysis.VersionConflicts.Count > 0)
        {
            console.WriteLine();
            console.Write(VersionConflictTable(result.Analysis.VersionConflicts));
        }

        console.WriteLine();
        if (result.AiResponse is { } ai)
        {
            console.Write(new Panel(MarkdownRenderer.Render(ai.Text))
            {
                Header = new PanelHeader($"[bold] AI diagnosis · {Markup.Escape(result.Provider.DisplayName())} · {Markup.Escape(ai.Model)} [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.MediumPurple1),
                Padding = new Padding(1, 1),
                Expand = true,
            });

            if (ai.WasTruncated)
            {
                console.MarkupLine("[yellow]warning:[/] the model hit its output limit; the answer may be cut off.");
            }

            if (result.Analysis.Hints.Count > 0)
            {
                console.WriteLine();
                console.Write(CompactHints(result.Analysis.Hints));
            }
        }
        else
        {
            if (result.AiError is not null)
            {
                console.MarkupLine($"[yellow]⚠ AI diagnosis unavailable:[/] {Markup.Escape(result.AiError)}");
                console.WriteLine();
            }

            RenderLocalHints(console, result.Analysis.Hints, log);
            if (result.Provider == ProviderKind.None && log.HasProblems && options.ProviderProblem is null)
            {
                console.MarkupLine("[grey]Tip: get an AI diagnosis with [bold]--provider ollama|anthropic|openai[/] (see [bold]dotnet-diag doctor[/])[/]");
            }
        }

        if (options.ShowContext)
        {
            console.WriteLine();
            console.Write(new Panel(new Text(result.Prompt.User, new Style(Color.Grey70)))
            {
                Header = new PanelHeader($"[grey] LLM context · ~{result.Bundle.EstimatedTokens}/{result.Bundle.TokenBudget} tokens [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = Theme.Border,
                Expand = true,
            });
        }

        console.WriteLine();
        console.MarkupLine(Footer(result));
    }

    public static Panel KnownErrorPanel(KnownError known)
    {
        var category = DiagnosticCategories.FromCode(known.Code);
        var rows = new List<IRenderable>
        {
            new Markup(Markup.Escape(known.Explanation)),
            Text.Empty,
            new Markup("[bold]Fixes[/]"),
        };
        rows.AddRange(known.Fixes.Select(f => new Markup($"  [green]→[/] {MarkdownRenderer.Inline(f)}")));
        rows.Add(Text.Empty);
        rows.Add(new Markup($"[grey]Docs:[/] [link]{Markup.Escape(known.DocsUrl)}[/]"));

        return new Panel(new Rows(rows))
        {
            Header = new PanelHeader($" {Theme.CodeMarkup(known.Code, category)} [bold]{Markup.Escape(known.Title)}[/] [grey]({Markup.Escape(category.DisplayName())})[/] "),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.CategoryColor(category)),
            Padding = new Padding(1, 0),
        };
    }

    private static Grid Summary(DiagnosisResult result)
    {
        var log = result.Log;
        var testsFailed = log.TestFailures.Count > 0 || log.TestSummary?.Failed > 0;
        var badge = (log.BuildSucceeded, testsFailed) switch
        {
            (false, _) => "[bold white on red] BUILD FAILED [/]",
            (_, true) => "[bold black on yellow] TESTS FAILED [/]",
            (true, false) => "[bold black on green] BUILD SUCCEEDED [/]",
            _ => log.ErrorCount > 0 ? "[bold white on red] ERRORS [/]" : "[bold black on grey] NO FAILURES FOUND [/]",
        };

        var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
        grid.AddRow(new Markup("[grey]Result[/]"), new Markup(badge));
        grid.AddRow(new Markup("[grey]Input[/]"), new Markup(Markup.Escape(log.KindDisplayName)));
        grid.AddRow(
            new Markup("[grey]Diagnostics[/]"),
            new Markup($"[red]{log.ErrorCount} error(s)[/]  [yellow]{log.WarningCount} warning(s)[/]"));

        if (log.TestSummary is { } t)
        {
            grid.AddRow(
                new Markup("[grey]Tests[/]"),
                new Markup($"[red]{t.Failed} failed[/]  [green]{t.Passed} passed[/]  [grey]{t.Skipped} skipped · {t.Total} total[/]"));
        }
        else if (log.TestFailures.Count > 0)
        {
            grid.AddRow(new Markup("[grey]Tests[/]"), new Markup($"[red]{log.TestFailures.Count} failed[/]"));
        }

        if (result.Workspace is { } ws)
        {
            var git = ws.IsGitRepository
                ? $"{Markup.Escape(ws.Root)} [grey]({Markup.Escape(ws.Branch ?? "detached")}, {ws.ModifiedFiles.Count} uncommitted)[/]"
                : Markup.Escape(ws.Root);
            grid.AddRow(new Markup("[grey]Workspace[/]"), new Markup(git));
        }

        return grid;
    }

    private static Table? DiagnosticsTable(ParsedLog log, bool includeWarnings, Func<string, string> rel, Func<string, string> relText)
    {
        var groups = log.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || includeWarnings)
            .GroupBy(d => (d.Code, d.Severity, Message: TextSanitizer.CollapseWhitespace(d.Message)))
            .OrderByDescending(g => g.Key.Severity)
            .ToList();
        if (groups.Count == 0)
        {
            return null;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(Theme.Border)
            .ShowRowSeparators()
            .Title("[bold]Diagnostics[/]")
            .AddColumn(new TableColumn(string.Empty).Centered())
            .AddColumn("[bold]Code[/]")
            .AddColumn("[bold]Category[/]")
            .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
            .AddColumn("[bold]Location[/]")
            .AddColumn("[bold]Message[/]");

        foreach (var group in groups.Take(MaxTableRows))
        {
            var first = group.First();
            var location = first.DisplayLocation(rel);

            table.AddRow(
                new Markup(Theme.SeverityIcon(first.Severity)),
                new Markup(Theme.CodeMarkup(first.Code, first.Category)),
                new Markup($"[grey]{Markup.Escape(first.Category.DisplayName())}[/]"),
                new Markup(group.Count().ToString(CultureInfo.InvariantCulture)),
                new Markup($"[link]{Markup.Escape(location)}[/]"),
                new Markup($"[{Theme.SeverityColor(first.Severity)}]{Markup.Escape(TextSanitizer.Truncate(relText(group.Key.Message), 140))}[/]"));
        }

        if (groups.Count > MaxTableRows)
        {
            table.Caption($"[grey]+{groups.Count - MaxTableRows} more (see --format json)[/]");
        }

        return table;
    }

    private static Table TestTable(ParsedLog log, Func<string, string> rel)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(Theme.Border)
            .ShowRowSeparators()
            .Title("[bold]Failing tests[/]")
            .AddColumn("[bold]Test[/]")
            .AddColumn("[bold]Failure[/]")
            .AddColumn("[bold]Location[/]");

        foreach (var failure in log.TestFailures.Take(MaxTableRows))
        {
            var frame = failure.FirstUserFrame;
            var location = frame?.File is null ? frame?.Method ?? string.Empty : $"{rel(frame.File)}:{frame.Line}";
            var message = TextSanitizer.Truncate(TextSanitizer.CollapseWhitespace(failure.ErrorMessage), 160);
            table.AddRow(
                new Markup($"[bold]{Markup.Escape(LocalDiagnosticEngine.ShortTestName(failure.TestName))}[/]{(failure.Duration is null ? string.Empty : $" [grey]{Markup.Escape(failure.Duration)}[/]")}"),
                new Markup($"[red]{Markup.Escape(message)}[/]"),
                new Markup(Markup.Escape(location)));
        }

        if (log.TestFailures.Count > MaxTableRows)
        {
            table.Caption($"[grey]+{log.TestFailures.Count - MaxTableRows} more[/]");
        }

        return table;
    }

    private static Table MissingPackagesTable(IReadOnlyList<MissingPackage> packages)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.MediumPurple1))
            .Title("[bold]Missing NuGet references[/]")
            .AddColumn("[bold]Package[/]")
            .AddColumn("[bold]Reason[/]")
            .AddColumn("[bold]Suggested fix[/]");

        foreach (var package in packages)
        {
            table.AddRow(
                new Markup($"[bold mediumpurple1]{Markup.Escape(package.Package)}[/]{(package.Version is null ? string.Empty : $" [grey]{Markup.Escape(package.Version)}[/]")}"),
                new Markup(Markup.Escape(MarkdownReportWriter.Describe(package.Reason))),
                new Markup($"[aqua]{Markup.Escape(package.SuggestedCommand)}[/]"));
        }

        return table;
    }

    private static Table VersionConflictTable(IReadOnlyList<VersionConflict> conflicts)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.DarkOrange))
            .Title("[bold]Version conflicts[/]")
            .AddColumn("[bold]Package / assembly[/]")
            .AddColumn("[bold]Source[/]")
            .AddColumn("[bold]Versions[/]")
            .AddColumn("[bold]Suggested fix[/]");

        foreach (var conflict in conflicts)
        {
            var recommended = conflict.RecommendedVersion;
            var versions = string.Join(" [grey]vs[/] ", conflict.Versions.Select(v =>
                v == recommended ? $"[green]{Markup.Escape(v)}[/]" : $"[red]{Markup.Escape(v)}[/]"));
            table.AddRow(
                new Markup($"[bold darkorange]{Markup.Escape(conflict.Name)}[/]"),
                new Markup(Theme.CodeMarkup(conflict.Code, DiagnosticCategories.FromCode(conflict.Code))),
                new Markup(versions.Length == 0 ? "[grey]?[/]" : versions),
                new Markup($"[aqua]{Markup.Escape(conflict.SuggestedCommand)}[/]"));
        }

        return table;
    }

    private static void RenderLocalHints(IAnsiConsole console, IReadOnlyList<LocalHint> hints, ParsedLog log)
    {
        if (hints.Count == 0)
        {
            console.MarkupLine(log.HasProblems
                ? "[yellow]No known patterns matched. Re-run with an AI provider or a binary log (-bl) for more detail.[/]"
                : "[green]✔ No errors or failing tests found.[/]");
            return;
        }

        console.Write(new Rule("[bold]Local diagnosis[/] [grey](offline heuristics)[/]") { Justification = Justify.Left, Style = Theme.Border });
        console.WriteLine();
        foreach (var hint in hints)
        {
            var category = DiagnosticCategories.FromCode(hint.Code);
            var rows = new List<IRenderable> { new Markup(MarkdownRenderer.Inline(hint.Summary)) };
            if (hint.Suggestions.Count > 0)
            {
                rows.Add(Text.Empty);
                rows.AddRange(hint.Suggestions.Select(s => new Markup($"[green]→[/] {MarkdownRenderer.Inline(s)}")));
            }

            if (hint.Locations.Count > 0)
            {
                rows.Add(Text.Empty);
                rows.Add(new Markup($"[grey]at[/] {string.Join("[grey],[/] ", hint.Locations.Select(l => $"[link]{Markup.Escape(l)}[/]"))}"));
            }

            var occurrences = hint.Occurrences > 1 ? $" [grey]×{hint.Occurrences}[/]" : string.Empty;
            var symptom = hint.IsLikelySymptom ? " [grey](likely a symptom)[/]" : string.Empty;
            console.Write(new Panel(new Rows(rows))
            {
                Header = new PanelHeader($" {Theme.SeverityIcon(hint.Severity)} {Theme.CodeMarkup(hint.Code, category)} [bold]{Markup.Escape(hint.Title)}[/]{occurrences}{symptom} "),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(hint.Severity == DiagnosticSeverity.Error ? Color.Red3 : Color.Yellow3),
                Padding = new Padding(1, 0),
                Expand = true,
            });
        }
    }

    private static Panel CompactHints(IReadOnlyList<LocalHint> hints)
    {
        var rows = hints.Take(5).Select(h =>
            (IRenderable)new Markup($"{Theme.SeverityIcon(h.Severity)} {Theme.CodeMarkup(h.Code, DiagnosticCategories.FromCode(h.Code))} {MarkdownRenderer.Inline(h.Summary)}"));
        return new Panel(new Rows(rows))
        {
            Header = new PanelHeader("[grey] Offline checks [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.Border,
            Padding = new Padding(1, 0),
            Expand = true,
        };
    }

    private static string Footer(DiagnosisResult result)
    {
        var parts = new List<string>
        {
            $"context ~{result.Bundle.EstimatedTokens}/{result.Bundle.TokenBudget} tokens{(result.Bundle.Truncated ? " (truncated)" : string.Empty)}",
        };
        if (result.AiResponse is { InputTokens: { } inTokens, OutputTokens: { } outTokens })
        {
            parts.Add($"model usage {inTokens} in / {outTokens} out");
        }

        parts.Add($"{result.Elapsed.TotalSeconds:0.0}s");
        return $"[grey]{Markup.Escape(string.Join(" · ", parts))}[/]";
    }
}
