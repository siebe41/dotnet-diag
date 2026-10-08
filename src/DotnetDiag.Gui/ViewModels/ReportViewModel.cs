using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotnetDiag.Core;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Reporting;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.ViewModels;

/// <param name="ShowWarnings">List warnings in the diagnostics, not just a summary (<c>--warnings</c>).</param>
/// <param name="ExitCodeGate">Report exit code 1 when problems were found (<c>--exit-code</c>).</param>
/// <param name="ProviderProblem">Why the selected provider could not be used, if it could not.</param>
public sealed record ReportOptions(bool ShowWarnings = false, bool ExitCodeGate = false, string? ProviderProblem = null);

public sealed record DiagnosticRow(
    bool IsError,
    Tone Severity,
    string Code,
    Tone CategoryTone,
    string Category,
    string? Count,
    string Location,
    string Message,
    string? File);

public sealed record TestRow(string Name, string FullName, string? Duration, string Message, string Location, string StackTrace)
{
    public bool HasStackTrace => StackTrace.Length > 0;
}

public sealed record PackageRow(string Package, string? Version, string Reason, string Command);

public sealed record VersionChip(string Version, Tone Tone);

public sealed record ConflictRow(string Name, string Code, Tone CodeTone, IReadOnlyList<VersionChip> Versions, string Command);

public sealed record HintRow(
    bool IsError,
    Tone Severity,
    string Code,
    Tone CategoryTone,
    string Title,
    string? Occurrences,
    bool IsLikelySymptom,
    string Summary,
    IReadOnlyList<string> Suggestions,
    string? Locations,
    string? DocsUrl);

/// <summary>One finished analysis, shaped for display: the desktop counterpart of the CLI's pretty report.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    /// <summary>Rows rendered at once. Logs with thousands of distinct warnings stay responsive; the filter reaches the rest.</summary>
    public const int MaxRows = 250;

    private readonly DiagnosisResult _result;
    private readonly IDesktopServices _desktop;
    private readonly Action<string>? _explain;
    private readonly List<DiagnosticRow> _allDiagnostics;
    private string? _markdown;
    private string? _json;

    [ObservableProperty]
    private bool _showWarnings;

    [ObservableProperty]
    private bool _exitCodeGate;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private string? _diagnosticsNote;

    [ObservableProperty]
    private string? _saveStatus;

    public ReportViewModel(DiagnosisResult result, ReportOptions options, IDesktopServices desktop, Action<string>? explain = null)
    {
        _result = result;
        _desktop = desktop;
        _explain = explain;
        _showWarnings = options.ShowWarnings;
        _exitCodeGate = options.ExitCodeGate;

        var log = result.Log;
        string Rel(string path) => result.Workspace?.ToRelative(path) ?? path.Replace('\\', '/');

        SourceName = log.SourceName;
        KindName = log.KindDisplayName;
        ErrorCount = log.ErrorCount;
        WarningCount = log.WarningCount;
        HasProblems = log.HasProblems;

        var testsFailed = log.TestFailures.Count > 0 || log.TestSummary?.Failed > 0;
        (BadgeText, BadgeTone) = (log.BuildSucceeded, testsFailed) switch
        {
            (false, _) => ("BUILD FAILED", Tone.Error),
            (_, true) => ("TESTS FAILED", Tone.Warning),
            (true, false) => ("BUILD SUCCEEDED", Tone.Success),
            _ => log.ErrorCount > 0 ? ("ERRORS", Tone.Error) : ("NO FAILURES FOUND", Tone.Neutral),
        };

        TestsText = log.TestSummary is { } tests
            ? $"{tests.Failed} failed · {tests.Passed} passed · {tests.Skipped} skipped · {tests.Total} total"
            : log.TestFailures.Count > 0 ? $"{log.TestFailures.Count} failed" : null;

        WorkspaceText = result.Workspace switch
        {
            { IsGitRepository: true } ws => $"{ws.Root} ({ws.Branch ?? "detached"}, {ws.ModifiedFiles.Count} uncommitted)",
            { } ws => ws.Root,
            _ => null,
        };

        _allDiagnostics = [.. log.Diagnostics
            .GroupBy(d => (d.Code, d.Severity, Message: TextSanitizer.CollapseWhitespace(d.Message)))
            .OrderByDescending(g => g.Key.Severity)
            .Select(group =>
            {
                var first = group.First();
                var count = group.Count();
                return new DiagnosticRow(
                    first.Severity == DiagnosticSeverity.Error,
                    Tones.For(first.Severity),
                    first.Code,
                    Tones.For(first.Category),
                    first.Category.DisplayName(),
                    count > 1 ? $"×{count}" : null,
                    first.DisplayLocation(Rel),
                    TextSanitizer.Truncate(result.Workspace?.RelativizeText(group.Key.Message) ?? group.Key.Message, 600),
                    first.IsInBuildTooling ? first.Project : first.File ?? first.Project);
            })];

        Tests = [.. log.TestFailures.Take(MaxRows).Select(failure =>
        {
            var frame = failure.FirstUserFrame;
            return new TestRow(
                LocalDiagnosticEngine.ShortTestName(failure.TestName),
                failure.TestName,
                failure.Duration,
                TextSanitizer.Truncate(TextSanitizer.CollapseWhitespace(failure.ErrorMessage), 600),
                frame?.File is null ? frame?.Method ?? string.Empty : $"{Rel(frame.File)}:{frame.Line}",
                string.Join('\n', failure.StackTrace.Select(f => "at " + f)));
        })];
        TestsNote = log.TestFailures.Count > MaxRows ? $"+{log.TestFailures.Count - MaxRows} more" : null;

        MissingPackages = [.. result.Analysis.MissingPackages.Select(package =>
            new PackageRow(package.Package, package.Version, MarkdownReportWriter.Describe(package.Reason), package.SuggestedCommand))];

        VersionConflicts = [.. result.Analysis.VersionConflicts.Select(conflict =>
        {
            var recommended = conflict.RecommendedVersion;
            IReadOnlyList<VersionChip> versions = conflict.Versions.Count == 0
                ? [new VersionChip("?", Tone.Neutral)]
                : [.. conflict.Versions.Select(v => new VersionChip(v, v == recommended ? Tone.Success : Tone.Error))];
            return new ConflictRow(conflict.Name, conflict.Code, Tones.ForCode(conflict.Code), versions, conflict.SuggestedCommand);
        })];

        Hints = [.. result.Analysis.Hints.Select(hint => new HintRow(
            hint.Severity == DiagnosticSeverity.Error,
            Tones.For(hint.Severity),
            hint.Code,
            Tones.ForCode(hint.Code),
            hint.Title,
            hint.Occurrences > 1 ? $"×{hint.Occurrences}" : null,
            hint.IsLikelySymptom,
            hint.Summary,
            hint.Suggestions,
            hint.Locations.Count > 0 ? string.Join(", ", hint.Locations) : null,
            hint.DocsUrl ?? KnownErrorCatalog.Find(hint.Code)?.DocsUrl))];

        if (result.AiResponse is { } ai)
        {
            AiTitle = $"AI diagnosis · {result.Provider.DisplayName()} · {ai.Model}";
            AiMarkdown = ai.Text;
            AiTruncatedText = ai.WasTruncated ? "The model hit its output limit; the answer may be cut off." : null;
            HintsTitle = "OFFLINE CHECKS";
        }
        else
        {
            AiError = result.AiError is null ? null : $"AI diagnosis unavailable: {result.AiError}";
            HintsTitle = "LOCAL DIAGNOSIS · OFFLINE HEURISTICS";
        }

        if (Hints.Count == 0)
        {
            (NoHintsText, NoHintsTone) = log.HasProblems
                ? ("No known patterns matched. Re-run with an AI provider or a binary log (-bl) for more detail.", Tone.Warning)
                : ("No errors or failing tests found.", Tone.Success);
        }

        TipText = result.Provider == ProviderKind.None && log.HasProblems && options.ProviderProblem is null
            ? "Tip: choose an AI provider under Run options to add a model-written diagnosis."
            : null;

        var bundle = result.Bundle;
        ContextTokens = bundle.EstimatedTokens;
        ContextBudget = bundle.TokenBudget;
        ContextSummary = $"~{bundle.EstimatedTokens} of {bundle.TokenBudget} tokens · "
                         + $"{bundle.IncludedErrorGroups}/{bundle.TotalErrorGroups} error groups · "
                         + $"{bundle.IncludedTestFailures}/{bundle.TotalTestFailures} failing tests"
                         + (bundle.Truncated ? " · truncated to fit" : string.Empty);
        ContextNote = (result.AiResponse, result.AiError) switch
        {
            ({ }, _) => $"This is exactly what was sent to {result.Provider.DisplayName()} ({result.Model}).",
            (null, { }) => $"This was sent to {result.Provider.DisplayName()} ({result.Model}), which did not return an answer.",
            _ => "Nothing was sent anywhere. This is exactly what would be sent to a model.",
        };

        RefreshDiagnostics();
    }

    public string SourceName { get; }

    public string KindName { get; }

    public string BadgeText { get; }

    public Tone BadgeTone { get; }

    public int ErrorCount { get; }

    public int WarningCount { get; }

    public bool HasProblems { get; }

    public string? TestsText { get; }

    public string? WorkspaceText { get; }

    /// <summary>The diagnostics that pass the filter and the warnings toggle.</summary>
    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = [];

    public bool HasDiagnostics => _allDiagnostics.Count > 0;

    public IReadOnlyList<TestRow> Tests { get; }

    public bool HasTests => Tests.Count > 0;

    public string? TestsNote { get; }

    public IReadOnlyList<PackageRow> MissingPackages { get; }

    public bool HasMissingPackages => MissingPackages.Count > 0;

    public IReadOnlyList<ConflictRow> VersionConflicts { get; }

    public bool HasVersionConflicts => VersionConflicts.Count > 0;

    public IReadOnlyList<HintRow> Hints { get; }

    public bool HasHints => Hints.Count > 0;

    public bool HasAi => AiMarkdown is not null;

    public string HintsTitle { get; }

    public string? NoHintsText { get; }

    public Tone NoHintsTone { get; }

    public string? TipText { get; }

    public string? AiTitle { get; }

    public string? AiMarkdown { get; }

    public string? AiTruncatedText { get; }

    public string? AiError { get; }

    public string PromptSystem => _result.Prompt.System;

    public string PromptUser => _result.Prompt.User;

    public int ContextTokens { get; }

    public int ContextBudget { get; }

    public string ContextSummary { get; }

    public string ContextNote { get; }

    /// <summary>The same text as <c>--format markdown</c>.</summary>
    public string Markdown => _markdown ??= MarkdownReportWriter.Write(_result);

    /// <summary>The same text as <c>--format json</c>.</summary>
    public string Json => _json ??= JsonReportWriter.Write(_result);

    /// <summary>What <c>dotnet-diag analyze</c> would return for this log.</summary>
    public int ExitCode => ExitCodeGate && HasProblems ? 1 : 0;

    public string ExitCodeText => (ExitCodeGate, HasProblems) switch
    {
        (true, _) => $"exit code {ExitCode}",
        (false, true) => "exit code 0 (1 with --exit-code)",
        _ => "exit code 0",
    };

    public string FooterText
    {
        get
        {
            var parts = new List<string>
            {
                $"context ~{_result.Bundle.EstimatedTokens}/{_result.Bundle.TokenBudget} tokens{(_result.Bundle.Truncated ? " (truncated)" : string.Empty)}",
            };
            if (_result.AiResponse is { InputTokens: { } input, OutputTokens: { } output })
            {
                parts.Add($"model usage {input} in / {output} out");
            }

            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{_result.Elapsed.TotalSeconds:0.0}s"));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>One line for the window's status bar.</summary>
    public string StatusLine
    {
        get
        {
            var parts = new List<string> { $"{ErrorCount} error(s)", $"{WarningCount} warning(s)" };
            if (TestsText is not null)
            {
                parts.Add("tests: " + TestsText);
            }

            return $"{SourceName} — {string.Join(", ", parts)}";
        }
    }

    partial void OnShowWarningsChanged(bool value) => RefreshDiagnostics();

    partial void OnFilterChanged(string value) => RefreshDiagnostics();

    partial void OnExitCodeGateChanged(bool value)
    {
        OnPropertyChanged(nameof(ExitCode));
        OnPropertyChanged(nameof(ExitCodeText));
    }

    [RelayCommand]
    private void Explain(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            _explain?.Invoke(code);
        }
    }

    /// <summary>Opens the file when it exists on this machine; otherwise puts the location on the clipboard.</summary>
    [RelayCommand]
    private async Task OpenLocationAsync(DiagnosticRow? row)
    {
        if (row is null || row.Location.Length == 0)
        {
            return;
        }

        // The path comes from a log, which is untrusted: only files inside the workspace are ever opened, never
        // an arbitrary local file or a network share that a crafted log happens to name.
        if (row.File is { } file && _result.Workspace is { } workspace && workspace.Resolve(file) is { } resolved
            && IsInside(workspace.Root, resolved) && _desktop.TryOpenSourceFile(resolved))
        {
            return;
        }

        await _desktop.CopyTextAsync(row.Location);
    }

    private static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    [RelayCommand]
    private Task SaveMarkdownAsync() => SaveAsync("md", Markdown);

    [RelayCommand]
    private Task SaveJsonAsync() => SaveAsync("json", Json);

    private async Task SaveAsync(string extension, string content)
    {
        var name = Path.GetFileNameWithoutExtension(SourceName.Replace(' ', '-')) + "-diagnosis." + extension;
        var path = await _desktop.PickSaveFileAsync("Save report", name, extension);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, content);
            SaveStatus = $"Saved {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveStatus = $"Could not save: {ex.Message}";
        }
    }

    private void RefreshDiagnostics()
    {
        var filter = Filter.Trim();
        var matching = _allDiagnostics
            .Where(row => row.IsError || ShowWarnings)
            .Where(row => filter.Length == 0
                          || row.Code.Contains(filter, StringComparison.OrdinalIgnoreCase)
                          || row.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)
                          || row.Location.Contains(filter, StringComparison.OrdinalIgnoreCase)
                          || row.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Diagnostics.Clear();
        foreach (var row in matching.Take(MaxRows))
        {
            Diagnostics.Add(row);
        }

        if (matching.Count > MaxRows)
        {
            DiagnosticsNote = $"+{matching.Count - MaxRows} more. Narrow the filter, or see the JSON tab for everything.";
        }
        else if (!ShowWarnings && WarningCount > 0)
        {
            var top = _result.Log.Warnings.GroupBy(w => w.Code).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key} ×{g.Count()}");
            DiagnosticsNote = $"{WarningCount} warning(s): {string.Join(", ", top)}. Turn on \"Show warnings\" to list them.";
        }
        else if (matching.Count == 0 && filter.Length > 0)
        {
            DiagnosticsNote = "Nothing matches the filter.";
        }
        else
        {
            DiagnosticsNote = null;
        }
    }
}
