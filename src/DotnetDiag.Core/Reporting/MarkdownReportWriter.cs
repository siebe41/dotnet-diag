using System.Globalization;
using System.Text;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;

namespace DotnetDiag.Core.Reporting;

/// <summary>Plain Markdown report, e.g. for <c>$GITHUB_STEP_SUMMARY</c> or a PR comment.</summary>
public static class MarkdownReportWriter
{
    public static string Write(DiagnosisResult result)
    {
        var log = result.Log;
        string Rel(string p) => result.Workspace?.ToRelative(p) ?? p.Replace('\\', '/');
        var sb = new StringBuilder();

        sb.Append(CultureInfo.InvariantCulture, $"# dotnet-diag: {log.SourceName}\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"**Input:** {log.KindDisplayName} · **Errors:** {log.ErrorCount} · **Warnings:** {log.WarningCount}");
        if (log.TestSummary is { } t)
        {
            sb.Append(CultureInfo.InvariantCulture, $" · **Tests:** {t.Failed} failed / {t.Total}");
        }
        else if (log.TestFailures.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $" · **Failed tests:** {log.TestFailures.Count}");
        }

        sb.Append("\n\n");

        var errors = log.Errors.ToList();
        if (errors.Count > 0)
        {
            sb.Append("## Errors\n\n| Code | Count | Location | Message |\n|---|---:|---|---|\n");
            foreach (var group in errors.GroupBy(e => e.Code))
            {
                var first = group.First();
                sb.Append(CultureInfo.InvariantCulture,
                    $"| `{group.Key}` | {group.Count()} | {Cell(first.DisplayLocation(Rel))} | {Cell(Parsing.TextSanitizer.Truncate(result.Workspace?.RelativizeText(first.Message) ?? first.Message, 160))} |\n");
            }

            sb.Append('\n');
        }

        if (log.TestFailures.Count > 0)
        {
            sb.Append("## Failing tests\n\n");
            foreach (var failure in log.TestFailures)
            {
                var frame = failure.FirstUserFrame;
                sb.Append(CultureInfo.InvariantCulture, $"- **{failure.TestName}**");
                if (frame?.File is not null)
                {
                    sb.Append(CultureInfo.InvariantCulture, $" ({Rel(frame.File)}:{frame.Line})");
                }

                var message = Parsing.TextSanitizer.Truncate(Parsing.TextSanitizer.CollapseWhitespace(failure.ErrorMessage), 200);
                sb.Append(CultureInfo.InvariantCulture, $"  \n  {message}\n");
            }

            sb.Append('\n');
        }

        WriteTables(sb, result.Analysis);

        if (result.AiResponse is { } ai)
        {
            sb.Append(CultureInfo.InvariantCulture, $"## AI diagnosis ({result.Provider} · {ai.Model})\n\n");
            sb.Append(ai.Text).Append("\n\n");
        }
        else
        {
            if (result.AiError is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $"> AI diagnosis unavailable: {result.AiError}\n\n");
            }

            sb.Append("## Local diagnosis\n\n");
            foreach (var hint in result.Analysis.Hints)
            {
                sb.Append(CultureInfo.InvariantCulture, $"### `{hint.Code}` {hint.Title}\n\n{hint.Summary}\n\n");
                foreach (var suggestion in hint.Suggestions)
                {
                    sb.Append("- ").Append(suggestion).Append('\n');
                }

                if (hint.Locations.Count > 0)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"\n_Locations:_ {string.Join(", ", hint.Locations.Select(l => $"`{l}`"))}\n");
                }

                sb.Append('\n');
            }

            if (result.Analysis.Hints.Count == 0)
            {
                sb.Append("No errors or failing tests found.\n");
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static void WriteTables(StringBuilder sb, LocalAnalysis analysis)
    {
        if (analysis.MissingPackages.Count > 0)
        {
            sb.Append("## Missing NuGet references\n\n| Package | Reason | Suggested fix |\n|---|---|---|\n");
            foreach (var m in analysis.MissingPackages)
            {
                sb.Append(CultureInfo.InvariantCulture, $"| `{m.Package}` | {Describe(m.Reason)} | `{Cell(m.SuggestedCommand)}` |\n");
            }

            sb.Append('\n');
        }

        if (analysis.VersionConflicts.Count > 0)
        {
            sb.Append("## Version conflicts\n\n| Package/assembly | Source | Versions | Suggested fix |\n|---|---|---|---|\n");
            foreach (var c in analysis.VersionConflicts)
            {
                sb.Append(CultureInfo.InvariantCulture, $"| `{c.Name}` | {c.Code} | {string.Join(", ", c.Versions)} | `{Cell(c.SuggestedCommand)}` |\n");
            }

            sb.Append('\n');
        }
    }

    public static string Describe(MissingPackageReason reason) => reason switch
    {
        MissingPackageReason.NotReferenced => "not referenced",
        MissingPackageReason.MissingUsing => "referenced, namespace not imported",
        MissingPackageReason.NotFoundInFeeds => "not found in NuGet feeds",
        MissingPackageReason.MissingTransitiveReference => "transitive assembly not referenced",
        _ => reason.ToString(),
    };

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}
