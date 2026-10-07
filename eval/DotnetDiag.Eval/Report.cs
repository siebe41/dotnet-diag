using System.Globalization;
using System.Text;
using DotnetDiag.Core.Providers;
using Spectre.Console;

namespace DotnetDiag.Eval;

internal sealed record Scorecard(
    int Scenarios,
    int Passed,
    int KnownGaps,
    (int Hit, int Total) Detection,
    (int Hit, int Total) RootCause,
    (int Hit, int Total) Advice,
    (int Hit, int Total) Packages,
    (int Hit, int Total) Fixes,
    (int Hit, int Total) Ai)
{
    public static Scorecard From(IReadOnlyList<ScenarioResult> results)
    {
        (int, int) Rate(string check)
        {
            var checks = results.SelectMany(r => r.AllChecks).Where(c => c.Name == check && c.Passed is not null).ToList();
            return (checks.Count(c => c.Passed == true), checks.Count);
        }

        return new Scorecard(
            results.Count,
            results.Count(r => r.Passed),
            results.Count(r => r.Scenario.KnownGap is not null),
            Rate("detected"),
            Rate("root cause first"),
            Rate("actionable advice"),
            Rate("packages"),
            Rate("fix verified"),
            Rate("ai answer"));
    }
}

internal static class Report
{
    public static Table SummaryTable(IReadOnlyList<ScenarioResult> results)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Scenarios[/]")
            .AddColumn("Scenario").AddColumn("Detect").AddColumn("Root cause").AddColumn("Packages").AddColumn("Advice").AddColumn("Fix works").AddColumn("Result");
        foreach (var r in results)
        {
            table.AddRow(
                Markup.Escape(r.Scenario.Name),
                Cell(r, "detected"),
                Cell(r, "root cause first"),
                Cell(r, "packages"),
                Cell(r, "actionable advice"),
                Mark(r.FixCheck.Passed),
                r.Passed ? "[green]pass[/]" : r.Scenario.KnownGap is null ? "[red]fail[/]" : "[yellow]known gap[/]");
        }

        return table;
    }

    public static Table ScorecardTable(Scorecard s)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Usefulness scorecard[/]").AddColumn("Metric").AddColumn(new TableColumn("Score").RightAligned()).AddColumn("Meaning");
        void Row(string metric, (int Hit, int Total) v, string meaning) =>
            table.AddRow(metric, v.Total == 0 ? "[grey]n/a[/]" : $"{v.Hit}/{v.Total} ({100.0 * v.Hit / v.Total:0}%)", $"[grey]{meaning}[/]");

        Row("Detection", s.Detection, "expected error codes / failing tests found");
        Row("Root cause ranked first", s.RootCause, "the first hint is the real cause, not a symptom");
        Row("Correct package advice", s.Packages, "right packages suggested, none invented");
        Row("Actionable advice", s.Advice, "suggestions contain the concrete fix");
        Row("Suggested fix actually works", s.Fixes, "applying the tool's dotnet add command makes the build pass");
        Row("AI answer on target", s.Ai, "AI diagnosis mentions the key fix (only with --provider)");
        table.AddRow("[bold]Scenarios passing[/]", $"[bold]{s.Passed}/{s.Scenarios}[/]", $"[grey]{s.KnownGaps} documented known gap(s)[/]");
        return table;
    }

    public static string Markdown(IReadOnlyList<ScenarioResult> results, Scorecard s, ProviderSettings settings, bool aiEnabled)
    {
        var sb = new StringBuilder();
        string Pct((int Hit, int Total) v) => v.Total == 0 ? "n/a" : $"{v.Hit}/{v.Total} ({100.0 * v.Hit / v.Total:0}%)";
        sb.Append(CultureInfo.InvariantCulture, $"# dotnet-diag usefulness report\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"- **OS:** {Environment.OSVersion} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})\n");
        sb.Append(CultureInfo.InvariantCulture, $"- **.NET runtime:** {Environment.Version}\n");
        sb.Append(CultureInfo.InvariantCulture, $"- **AI:** {(aiEnabled ? $"{settings.Kind.DisplayName()} · {settings.Model}" : "off (offline heuristics only)")}\n\n");

        sb.Append("## Scorecard\n\n| Metric | Score |\n|---|---:|\n");
        sb.Append(CultureInfo.InvariantCulture, $"| Detection | {Pct(s.Detection)} |\n| Root cause ranked first | {Pct(s.RootCause)} |\n| Correct package advice (none invented) | {Pct(s.Packages)} |\n| Actionable advice | {Pct(s.Advice)} |\n| **Suggested fix actually works** | **{Pct(s.Fixes)}** |\n| AI answer on target | {Pct(s.Ai)} |\n| Scenarios passing | {s.Passed}/{s.Scenarios} ({s.KnownGaps} known gaps) |\n\n");

        sb.Append("## Scenarios\n\n| Scenario | Result | Fix works | Time |\n|---|---|---|---:|\n");
        foreach (var r in results)
        {
            var result = r.Passed ? "✅ pass" : r.Scenario.KnownGap is null ? "❌ fail" : "🟡 known gap";
            sb.Append(CultureInfo.InvariantCulture, $"| `{r.Scenario.Name}` | {result} | {MdMark(r.FixCheck.Passed)} | {r.Elapsed.TotalSeconds:0.0}s |\n");
        }

        foreach (var r in results)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n### `{r.Scenario.Name}`\n\n{r.Scenario.Description}\n\n");
            if (r.Scenario.KnownGap is { } gap)
            {
                sb.Append(CultureInfo.InvariantCulture, $"> **Known gap:** {gap}\n\n");
            }

            sb.Append("| Input | Check | Result | Detail |\n|---|---|---|---|\n");
            foreach (var input in r.Inputs)
            {
                foreach (var check in input.Checks)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"| {input.Input} | {check.Name} | {MdMark(check.Passed)} | {Escape(check.Detail)} |\n");
                }
            }

            sb.Append(CultureInfo.InvariantCulture, $"| - | fix verified | {MdMark(r.FixCheck.Passed)} | {Escape(r.FixCheck.Detail)} |\n");

            var top = r.Inputs.Select(i => i.Diagnosis).FirstOrDefault(d => d is not null)?.Analysis.Hints.FirstOrDefault();
            if (top is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $"\n**Top hint:** `{top.Code}` {Escape(top.Title)} — {Escape(top.Summary)}\n");
            }

            foreach (var input in r.Inputs.Where(i => i.Diagnosis?.AiResponse is not null))
            {
                sb.Append(CultureInfo.InvariantCulture, $"\n<details><summary>AI answer ({input.Input})</summary>\n\n{input.Diagnosis!.AiResponse!.Text}\n\n</details>\n");
            }
        }

        return sb.ToString();
    }

    private static string Cell(ScenarioResult r, string check)
    {
        var checks = r.Inputs.SelectMany(i => i.Checks).Where(c => c.Name == check).ToList();
        return checks.Count == 0 ? "[grey]–[/]" : Mark(checks.All(c => c.Passed == true) ? true : checks.Any(c => c.Passed == false) ? false : null);
    }

    private static string Mark(bool? passed) => passed switch
    {
        true => "[green]✔[/]",
        false => "[red]✖[/]",
        null => "[grey]–[/]",
    };

    private static string MdMark(bool? passed) => passed switch
    {
        true => "✅",
        false => "❌",
        null => "–",
    };

    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}
