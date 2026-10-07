using System.Globalization;
using System.Text;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Core.Context;

/// <summary>
/// Turns a parsed log into a compact Markdown bundle that fits a token budget. Content is added in priority
/// order (summary → root errors → failing tests → source → package references → warnings) and anything
/// that does not fit is counted and reported as omitted rather than silently dropped.
/// </summary>
public sealed class ContextBuilder(ContextBuilderOptions? options = null)
{
    private const int FooterReserveTokens = 30;

    private static readonly HashSet<string> PackageRelatedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS0012", "CS0234", "CS0246", "CS1061", "CS1705", "MSB3277", "NU1101", "NU1102", "NU1107", "NU1605", "NU1608",
    };

    private readonly ContextBuilderOptions _options = options ?? new ContextBuilderOptions();

    public DiagnosticBundle Build(ParsedLog log, LocalAnalysis analysis, GitWorkspace? workspace = null)
    {
        var budget = Math.Max(100, _options.MaxTokens);
        var writer = new BudgetedWriter(budget - FooterReserveTokens);
        var sources = new SourceContextProvider(workspace);
        string Rel(string path) => workspace?.ToRelative(path) ?? path.Replace('\\', '/');

        writer.TryAppend(BuildSummary(log, workspace));

        // Errors: group identical code+message, rank by root-cause priority, keep first-seen order otherwise.
        var priorities = analysis.Hints
            .GroupBy(h => h.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(h => h.Priority), StringComparer.OrdinalIgnoreCase);
        var errorGroups = log.Errors
            .Select((d, index) => (d, index))
            .GroupBy(x => (x.d.Code, Message: Normalize(x.d.Message)))
            .Select(g => (Items: g.Select(x => x.d).ToList(), First: g.Min(x => x.index)))
            .OrderBy(g => priorities.GetValueOrDefault(g.Items[0].Code, 2))
            .ThenBy(g => g.First)
            .Select(g => g.Items)
            .ToList();

        var includedErrors = 0;
        foreach (var group in errorGroups)
        {
            var heading = includedErrors == 0 ? $"\n## Errors ({errorGroups.Count} unique)\n" : string.Empty;
            if (!writer.TryAppend(heading + FormatErrorGroup(group, Rel, workspace)))
            {
                break;
            }

            includedErrors++;
        }

        var includedTests = 0;
        foreach (var failure in log.TestFailures)
        {
            var heading = includedTests == 0 ? $"\n## Failing tests ({log.TestFailures.Count})\n" : string.Empty;
            if (!writer.TryAppend(heading + FormatTestFailure(failure, Rel)))
            {
                break;
            }

            includedTests++;
        }

        var snippets = new List<SourceSnippet>();
        var snippetHeadingWritten = false;
        foreach (var (file, line) in SnippetCandidates(log, errorGroups.Take(Math.Max(includedErrors, 1)), workspace))
        {
            if (snippets.Count >= _options.MaxSnippets)
            {
                break;
            }

            if (snippets.Any(s => s.RelativePath == Rel(file) && Math.Abs(s.TargetLine - line) <= _options.SnippetRadius))
            {
                continue;
            }

            foreach (var radius in new[] { _options.SnippetRadius, 1 })
            {
                var snippet = sources.GetSnippet(file, line, radius);
                if (snippet is null)
                {
                    break;
                }

                var heading = snippetHeadingWritten ? string.Empty : "\n## Source\n";
                var block = $"{heading}{snippet.RelativePath}:{line}{(snippet.IsModified ? " (uncommitted changes)" : string.Empty)}\n{snippet.ToCodeBlock()}\n";
                if (writer.TryAppend(block))
                {
                    snippetHeadingWritten = true;
                    snippets.Add(snippet);
                    break;
                }
            }
        }

        var packageSection = BuildPackageSection(log, workspace);
        if (packageSection is not null)
        {
            writer.TryAppend(packageSection);
        }

        var warningSection = BuildWarningSection(log);
        if (warningSection is not null)
        {
            writer.TryAppend(warningSection);
        }

        var omittedErrors = errorGroups.Count - includedErrors;
        var omittedTests = log.TestFailures.Count - includedTests;
        if (omittedErrors > 0 || omittedTests > 0)
        {
            var parts = new List<string>();
            if (omittedErrors > 0)
            {
                parts.Add($"{omittedErrors} more error group(s)");
            }

            if (omittedTests > 0)
            {
                parts.Add($"{omittedTests} more failing test(s)");
            }

            writer.ForceAppend($"\n(+{string.Join(", ", parts)} omitted to fit the {budget}-token budget)\n", budget);
        }

        var text = writer.ToString().Trim();
        return new DiagnosticBundle(
            text,
            TokenEstimator.Estimate(text),
            budget,
            includedErrors,
            errorGroups.Count,
            includedTests,
            log.TestFailures.Count,
            snippets);
    }

    private static string BuildSummary(ParsedLog log, GitWorkspace? workspace)
    {
        var result = log.BuildSucceeded switch
        {
            false => "FAILED",
            true when log.TestFailures.Count > 0 || log.TestSummary?.Failed > 0 => "build OK, tests FAILED",
            true => "succeeded",
            null when log.TestFailures.Count > 0 || log.TestSummary?.Failed > 0 => "tests FAILED",
            null => "unknown",
        };

        var sb = new StringBuilder();
        sb.Append("## Summary\n");
        sb.Append(CultureInfo.InvariantCulture, $"input: {log.KindDisplayName} ({log.SourceName}) | result: {result} | errors: {log.ErrorCount} | warnings: {log.WarningCount}");
        if (log.TestSummary is { } t)
        {
            sb.Append(CultureInfo.InvariantCulture, $" | tests: {t.Failed} failed, {t.Passed} passed, {t.Skipped} skipped of {t.Total}");
        }
        else if (log.TestFailures.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $" | tests failed: {log.TestFailures.Count}");
        }

        sb.Append('\n');
        if (workspace?.IsGitRepository == true)
        {
            var modified = workspace.ModifiedFiles.Count;
            sb.Append(CultureInfo.InvariantCulture, $"repo: branch {workspace.Branch ?? "?"}, {modified} file(s) with uncommitted changes\n");
        }

        return sb.ToString();
    }

    private string FormatErrorGroup(IReadOnlyList<BuildDiagnostic> group, Func<string, string> rel, GitWorkspace? workspace)
    {
        var first = group[0];
        var locations = group.Select(d => d.DisplayLocation(rel)).Where(l => l.Length > 0).Distinct().ToList();
        var location = locations.Count switch
        {
            0 => string.Empty,
            1 => locations[0],
            _ => $"{locations[0]} (+{locations.Count - 1} more: {string.Join(", ", locations.Skip(1).Take(2))}{(locations.Count > 3 ? ", …" : string.Empty)})",
        };

        var message = TextSanitizer.Truncate(Normalize(workspace?.RelativizeText(first.Message) ?? first.Message), _options.MaxMessageLength);
        return location.Length == 0
            ? $"- {first.Code}: {message}\n"
            : $"- {first.Code} {location}: {message}\n";
    }

    private string FormatTestFailure(TestFailure failure, Func<string, string> rel)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"### {failure.TestName}\n");
        var messageLines = failure.ErrorMessage
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(5);
        var message = TextSanitizer.Truncate(string.Join(" / ", messageLines), 320);
        if (message.Length > 0)
        {
            sb.Append(message).Append('\n');
        }

        var frames = failure.StackTrace.Where(f => f.IsUserCode).Take(_options.MaxFramesPerTest).ToList();
        if (frames.Count == 0)
        {
            frames = failure.StackTrace.Take(2).ToList();
        }

        foreach (var frame in frames)
        {
            sb.Append("  at ").Append(frame.Method);
            if (frame.File is not null)
            {
                sb.Append(" in ").Append(rel(frame.File));
                if (frame.Line is { } line)
                {
                    sb.Append(':').Append(line.ToString(CultureInfo.InvariantCulture));
                }
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static IEnumerable<(string File, int Line)> SnippetCandidates(
        ParsedLog log,
        IEnumerable<List<BuildDiagnostic>> includedGroups,
        GitWorkspace? workspace)
    {
        foreach (var group in includedGroups)
        {
            var d = group.FirstOrDefault(x => x.File is not null && x.Line > 0);
            if (d is not null && IsInWorkspace(d.File!, workspace) && !MsBuildTextParser.IsProjectFile(d.File!))
            {
                yield return (d.File!, d.Line!.Value);
            }
        }

        foreach (var failure in log.TestFailures)
        {
            var frame = failure.FirstUserFrame;
            if (frame?.File is not null && frame.Line > 0 && IsInWorkspace(frame.File, workspace))
            {
                yield return (frame.File, frame.Line!.Value);
            }
        }
    }

    private static bool IsInWorkspace(string file, GitWorkspace? workspace)
    {
        if (workspace is null)
        {
            return true;
        }

        return workspace.Contains(file);
    }

    private static string? BuildPackageSection(ParsedLog log, GitWorkspace? workspace)
    {
        var projects = log.Diagnostics
            .Where(d => PackageRelatedCodes.Contains(d.Code))
            .Select(d => d.Project ?? (d.File is not null && workspace?.Resolve(d.File) is { } file ? ProjectInspector.FindOwningProject(file, workspace.Root) : null))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (projects.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var project in projects)
        {
            var path = workspace?.Resolve(project) ?? (File.Exists(project) ? project : null);
            if (path is null)
            {
                continue;
            }

            var references = ProjectInspector.GetPackageReferences(path);
            var name = workspace?.ToRelative(path) ?? Path.GetFileName(path);
            var list = references.Count == 0
                ? "(none)"
                : string.Join(", ", references.Take(15).Select(r => r.Version is null ? r.Name : $"{r.Name} {r.Version}"));
            sb.Append(CultureInfo.InvariantCulture, $"- {name}: {list}\n");
        }

        return sb.Length == 0 ? null : "\n## Package references\n" + sb;
    }

    private static string? BuildWarningSection(ParsedLog log)
    {
        var top = log.Warnings
            .GroupBy(w => w.Code)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();
        return top.Count == 0 ? null : $"\n## Warnings (top codes)\n{string.Join(", ", top)}\n";
    }

    private static string Normalize(string message) => TextSanitizer.CollapseWhitespace(message);

    /// <summary>Appends blocks only while the running token estimate stays within the budget.</summary>
    private sealed class BudgetedWriter(int budgetTokens)
    {
        private readonly StringBuilder _sb = new();

        public bool TryAppend(string block)
        {
            if (TokenEstimator.Estimate(_sb.Length + block.Length) > budgetTokens)
            {
                return false;
            }

            _sb.Append(block);
            return true;
        }

        public void ForceAppend(string block, int hardLimitTokens)
        {
            if (TokenEstimator.Estimate(_sb.Length + block.Length) <= hardLimitTokens)
            {
                _sb.Append(block);
            }
        }

        public override string ToString() => _sb.ToString();
    }
}
