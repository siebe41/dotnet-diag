using System.Globalization;
using System.Text;
using DotnetDiag.Core;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Eval;

/// <summary>
/// Field trial: run dotnet-diag over a folder of real-world logs (no expectations) and produce a review sheet
/// that developers grade by hand. This is the ground truth for "is it actually useful?".
/// </summary>
internal static class FieldTrial
{
    private static readonly string[] Extensions = [".binlog", ".log", ".txt"];

    public static async Task<int> RunAsync(string logsDirectory, string? repo, ILlmProvider? provider, ContextBuilderOptions contextOptions, string reportPath)
    {
        var files = Directory.GetFiles(logsDirectory, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No .binlog/.log/.txt files in {logsDirectory}");
            return 2;
        }

        var workspace = repo is null ? null : GitWorkspace.ForDirectory(repo);
        var pipeline = new DiagnosisPipeline(provider, contextOptions);
        var rows = new List<(string File, DiagnosisResult? Result, string? Error)>();
        foreach (var file in files)
        {
            Console.WriteLine($"analyzing {Path.GetRelativePath(logsDirectory, file)}");
            try
            {
                rows.Add((file, await pipeline.AnalyzeFileAsync(file, workspace), null));
            }
            catch (Exception ex) when (ex is DotnetDiag.Core.Parsing.LogParseException or IOException)
            {
                rows.Add((file, null, ex.Message));
            }
        }

        var md = new StringBuilder();
        md.Append("# dotnet-diag field trial review sheet\n\n");
        md.Append(CultureInfo.InvariantCulture, $"{rows.Count} real logs · AI: {(provider is null ? "off" : $"{provider.Kind.DisplayName()} {provider.Model}")} · {DateTime.UtcNow:yyyy-MM-dd}\n\n");
        md.Append("For each log, the developer who knows the real cause fills in the grading columns:\n\n");
        md.Append("- **Root cause?** `Y` if the first hint (or the AI answer) names the actual cause, `P` if partially, `N` if wrong or missing\n");
        md.Append("- **Fix worked?** `Y` if following the suggestion as written fixed it, `N` if not, `-` if no fix was suggested\n");
        md.Append("- **Minutes saved** compared with reading the raw log yourself (negative if it misled you)\n\n");
        md.Append("| # | Log | Errors | Failed tests | Top hint | Suggested fix | Root cause? | Fix worked? | Minutes saved | Notes |\n");
        md.Append("|---:|---|---:|---:|---|---|:---:|:---:|---:|---|\n");

        var csv = new StringBuilder("index,log,errors,failed_tests,top_hint_code,top_hint,suggested_fix,root_cause_correct,fix_worked,minutes_saved,notes\n");
        for (var i = 0; i < rows.Count; i++)
        {
            var (file, result, error) = rows[i];
            var name = Path.GetRelativePath(logsDirectory, file);
            var top = result?.Analysis.Hints.FirstOrDefault(h => !h.IsLikelySymptom);
            var fix = result is null ? null : ScenarioRunner.FixCommands(result.Analysis).FirstOrDefault()
                ?? top?.Suggestions.FirstOrDefault();
            var hint = error is not null ? $"parse error: {error}" : top is null ? "(no hint)" : $"`{top.Code}` {top.Summary}";
            md.Append(CultureInfo.InvariantCulture,
                $"| {i + 1} | `{name}` | {result?.Log.ErrorCount} | {result?.Log.TestFailures.Count} | {Cell(hint)} | {Cell(fix ?? "")} | | | | |\n");
            csv.Append(CultureInfo.InvariantCulture,
                $"{i + 1},{Csv(name)},{result?.Log.ErrorCount},{result?.Log.TestFailures.Count},{Csv(top?.Code ?? "")},{Csv(top?.Summary ?? error ?? "")},{Csv(fix ?? "")},,,,\n");
        }

        foreach (var (file, result, _) in rows.Where(r => r.Result?.AiResponse is not null))
        {
            md.Append(CultureInfo.InvariantCulture, $"\n<details><summary>AI answer: {Path.GetRelativePath(logsDirectory, file)}</summary>\n\n{result!.AiResponse!.Text}\n\n</details>\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, md.ToString());
        File.WriteAllText(Path.ChangeExtension(reportPath, ".csv"), csv.ToString());
        Console.WriteLine($"Review sheet: {reportPath} (and {Path.ChangeExtension(reportPath, ".csv")})");
        return 0;
    }

    private static string Cell(string text) =>
        DotnetDiag.Core.Parsing.TextSanitizer.Truncate(text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' '), 160);

    private static string Csv(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal).Replace('\n', ' ') + "\"";
}
