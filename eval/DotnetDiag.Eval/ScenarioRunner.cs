using System.Diagnostics;
using DotnetDiag.Core;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Eval;

internal sealed record Check(string Name, bool? Passed, string Detail)
{
    public static Check Pass(string name, string detail = "") => new(name, true, detail);

    public static Check Fail(string name, string detail) => new(name, false, detail);

    public static Check Skip(string name, string detail) => new(name, null, detail);
}

internal sealed record InputResult(string Input, IReadOnlyList<Check> Checks, DiagnosisResult? Diagnosis, string? Error);

internal sealed record ScenarioResult(
    Scenario Scenario,
    ProcessResult? Build,
    IReadOnlyList<InputResult> Inputs,
    Check FixCheck,
    IReadOnlyList<string> AppliedCommands,
    string WorkDirectory,
    TimeSpan Elapsed)
{
    public IEnumerable<Check> AllChecks => Inputs.SelectMany(i => i.Checks).Append(FixCheck);

    public bool Passed => AllChecks.All(c => c.Passed != false);
}

/// <summary>Copies a scenario to a temp folder, breaks the build for real, diagnoses it, and verifies the fix.</summary>
internal sealed class ScenarioRunner(ILlmProvider? provider, ContextBuilderOptions contextOptions, bool keepWorkDirectories)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
    private readonly string _dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    public async Task<ScenarioResult> RunAsync(Scenario scenario, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var work = Path.Combine(Path.GetTempPath(), "dotnet-diag-eval", $"{scenario.Name}-{Guid.NewGuid().ToString("N")[..8]}");
        CopyScenario(scenario.Directory, work);
        await InitGitAsync(work, cancellationToken);
        var workspace = GitWorkspace.Discover(work) ?? GitWorkspace.ForDirectory(work);

        try
        {
            var build = await RunScenarioCommandAsync(scenario, work, binlog: true, cancellationToken);
            File.WriteAllText(Path.Combine(work, "console-output.txt"), build.Output);
            var inputs = new List<InputResult>();
            if (build.ExitCode == 0)
            {
                var check = Check.Fail("broken", "the scenario built successfully; it must fail before it can be diagnosed");
                return new ScenarioResult(scenario, build, [new InputResult("-", [check], null, null)], Check.Skip("fix verified", "n/a"), [], work, stopwatch.Elapsed);
            }

            foreach (var input in scenario.Inputs)
            {
                inputs.Add(await AnalyzeAsync(scenario, input, work, workspace, cancellationToken));
            }

            var (fixCheck, applied) = scenario.Fix.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? await VerifyFixAsync(scenario, work, inputs.FirstOrDefault(i => i.Diagnosis is not null)?.Diagnosis, cancellationToken)
                : (Check.Skip("fix verified", "no automatic fix expected"), (IReadOnlyList<string>)[]);

            return new ScenarioResult(scenario, build, inputs, fixCheck, applied, work, stopwatch.Elapsed);
        }
        finally
        {
            if (!keepWorkDirectories)
            {
                TryDelete(work);
            }
        }
    }

    /// <summary>
    /// Builds the broken scenario once and keeps its outputs as sample inputs: the MSBuild binary log (when the
    /// build gets far enough to write one) and the console output exactly as a developer would see it.
    /// </summary>
    public async Task<(ProcessResult Build, string? Binlog, string Text)> GenerateSampleAsync(Scenario scenario, string outputDirectory, CancellationToken cancellationToken)
    {
        // A stable folder name keeps the generated logs free of random GUIDs.
        var work = Path.Combine(Path.GetTempPath(), "dotnet-diag-samples", scenario.Name);
        TryDelete(work);
        CopyScenario(scenario.Directory, work);
        try
        {
            var build = await RunScenarioCommandAsync(scenario, work, binlog: true, cancellationToken);
            System.IO.Directory.CreateDirectory(outputDirectory);

            var text = Path.Combine(outputDirectory, $"{scenario.Name}.txt");
            File.WriteAllText(text, build.Output);

            string? binlog = null;
            var producedBinlog = Path.Combine(work, "msbuild.binlog");
            if (File.Exists(producedBinlog))
            {
                binlog = Path.Combine(outputDirectory, $"{scenario.Name}.binlog");
                File.Copy(producedBinlog, binlog, overwrite: true);
            }

            return (build, binlog, text);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private async Task<InputResult> AnalyzeAsync(Scenario scenario, string input, string work, GitWorkspace workspace, CancellationToken cancellationToken)
    {
        var pipeline = new DiagnosisPipeline(provider, contextOptions);
        DiagnosisResult result;
        try
        {
            result = input switch
            {
                "binlog" => await pipeline.AnalyzeFileAsync(Path.Combine(work, "msbuild.binlog"), workspace, cancellationToken: cancellationToken),
                _ => await pipeline.AnalyzeFileAsync(Path.Combine(work, "console-output.txt"), workspace, cancellationToken: cancellationToken),
            };
        }
        catch (Exception ex) when (ex is DotnetDiag.Core.Parsing.LogParseException or IOException)
        {
            return new InputResult(input, [Check.Fail("parse", ex.Message)], null, ex.Message);
        }

        return new InputResult(input, Score(scenario.Expect, result), result, null);
    }

    internal static List<Check> Score(Expectations expect, DiagnosisResult result)
    {
        var checks = new List<Check>();
        var log = result.Log;
        var analysis = result.Analysis;
        var hintCodes = analysis.Hints.Select(h => h.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Detection
        if (expect.HasDetectionChecks)
        {
            var missingCodes = expect.Codes.Where(c => !log.Diagnostics.Any(d => d.Code.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
            var missingHints = expect.HintCodes.Where(c => !hintCodes.Contains(c)).ToList();
            var testsOk = expect.TestFailures is not { } n || log.TestFailures.Count == n;
            checks.Add(missingCodes.Count == 0 && missingHints.Count == 0 && testsOk
                ? Check.Pass("detected", string.Join(", ", expect.Codes.Concat(expect.HintCodes).Concat(expect.TestFailures is { } t ? [$"{t} failing tests"] : [])))
                : Check.Fail("detected", $"missing codes [{string.Join(", ", missingCodes.Concat(missingHints))}]; found [{string.Join(", ", log.Diagnostics.Select(d => d.Code).Distinct())}]; tests failed: {log.TestFailures.Count}"));
        }

        // Root cause ranking
        if (expect.TopHint is { } top)
        {
            var first = analysis.Hints.FirstOrDefault(h => !h.IsLikelySymptom);
            checks.Add(first?.Code.Equals(top, StringComparison.OrdinalIgnoreCase) == true
                ? Check.Pass("root cause first", top)
                : Check.Fail("root cause first", $"expected {top}, got {first?.Code ?? "(no hints)"}"));
        }

        // Package suggestions: exact set, so invented packages count as failures.
        if (expect.MissingPackages is { } packages)
        {
            var actual = analysis.MissingPackages.Select(m => m.Package).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
            var wanted = packages.Order().ToArray();
            checks.Add(actual.SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase)
                ? Check.Pass("packages", wanted.Length == 0 ? "none suggested (correct)" : string.Join(", ", wanted))
                : Check.Fail("packages", $"expected [{string.Join(", ", wanted)}], suggested [{string.Join(", ", actual)}]"));
        }

        if (expect.VersionConflicts.Length > 0)
        {
            var missing = expect.VersionConflicts.Where(v => !analysis.VersionConflicts.Any(c => c.Name.Equals(v, StringComparison.OrdinalIgnoreCase))).ToList();
            checks.Add(missing.Count == 0
                ? Check.Pass("version conflicts", string.Join(", ", expect.VersionConflicts))
                : Check.Fail("version conflicts", $"not reported: {string.Join(", ", missing)}"));
        }

        // Actionable advice
        if (expect.SuggestionContains.Length > 0)
        {
            var advice = string.Join('\n', analysis.Hints.SelectMany(h => h.Suggestions.Append(h.Summary).Concat(h.Locations))
                .Concat(analysis.MissingPackages.Select(m => m.SuggestedCommand))
                .Concat(analysis.VersionConflicts.Select(c => c.SuggestedCommand)))
                .Replace("`", string.Empty, StringComparison.Ordinal);
            var missing = expect.SuggestionContains.Where(s => !advice.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
            checks.Add(missing.Count == 0
                ? Check.Pass("actionable advice", string.Join(" | ", expect.SuggestionContains))
                : Check.Fail("actionable advice", $"advice never mentions: {string.Join(" | ", missing)}"));
        }

        checks.Add(result.Bundle.EstimatedTokens <= result.Bundle.TokenBudget
            ? Check.Pass("context budget", $"~{result.Bundle.EstimatedTokens}/{result.Bundle.TokenBudget} tokens")
            : Check.Fail("context budget", $"~{result.Bundle.EstimatedTokens}/{result.Bundle.TokenBudget} tokens"));

        // AI (only when a provider is configured)
        if (result.AiResponse is { } ai)
        {
            var missing = expect.AiKeywords.Where(k => !ai.Text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
            checks.Add(missing.Count == 0
                ? Check.Pass("ai answer", $"{ai.OutputTokens?.ToString() ?? "?"} output tokens, mentions {string.Join(", ", expect.AiKeywords)}")
                : Check.Fail("ai answer", $"answer never mentions: {string.Join(", ", missing)}"));
        }
        else if (result.AiError is { } error)
        {
            checks.Add(Check.Fail("ai answer", error));
        }

        return checks;
    }

    /// <summary>Do exactly what the tool told the developer to do, then build again.</summary>
    private async Task<(Check, IReadOnlyList<string>)> VerifyFixAsync(Scenario scenario, string work, DiagnosisResult? diagnosis, CancellationToken cancellationToken)
    {
        if (diagnosis is null)
        {
            return (Check.Fail("fix verified", "no diagnosis to take a fix from"), []);
        }

        var commands = FixCommands(diagnosis.Analysis).ToList();
        if (commands.Count == 0)
        {
            return (Check.Fail("fix verified", "the tool suggested no runnable `dotnet add` command"), []);
        }

        foreach (var command in commands)
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var run = await ProcessRunner.RunAsync(_dotnet, parts.Skip(1), work, CommandTimeout, cancellationToken);
            if (run.ExitCode != 0)
            {
                return (Check.Fail("fix verified", $"`{command}` failed: {LastLines(run.Output, 3)}"), commands);
            }
        }

        var rebuild = await RunScenarioCommandAsync(scenario, work, binlog: false, cancellationToken);
        return rebuild.ExitCode == 0
            ? (Check.Pass("fix verified", $"{scenario.Command} passes after: {string.Join("; ", commands)}"), commands)
            : (Check.Fail("fix verified", $"{scenario.Command} still fails after the suggested fix: {LastLines(rebuild.Output, 4)}"), commands);
    }

    /// <summary>Runnable commands the tool proposed: structured package fixes first, then commands quoted in hints.</summary>
    internal static IEnumerable<string> FixCommands(LocalAnalysis analysis)
    {
        var candidates = analysis.MissingPackages.Select(m => m.SuggestedCommand)
            .Concat(analysis.VersionConflicts.Select(c => c.SuggestedCommand))
            .Concat(analysis.Hints.Where(h => !h.IsLikelySymptom).SelectMany(h => h.Suggestions).SelectMany(QuotedCommands));

        return candidates
            .Select(c => c.Trim().TrimEnd('.'))
            .Where(c => c.StartsWith("dotnet add ", StringComparison.Ordinal) && !c.Contains('<', StringComparison.Ordinal) && !c.Contains('#', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> QuotedCommands(string text)
    {
        var parts = text.Split('`');
        for (var i = 1; i < parts.Length; i += 2)
        {
            yield return parts[i];
        }
    }

    private Task<ProcessResult> RunScenarioCommandAsync(Scenario scenario, string work, bool binlog, CancellationToken cancellationToken)
    {
        List<string> args = [scenario.Command, scenario.Target];
        if (binlog && scenario.Inputs.Contains("binlog"))
        {
            args.Add("-bl:msbuild.binlog");
        }

        return ProcessRunner.RunAsync(_dotnet, args, work, CommandTimeout, cancellationToken);
    }

    private static async Task InitGitAsync(string work, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessRunner.RunAsync("git", ["init", "-q", "-b", "eval"], work, TimeSpan.FromSeconds(30), cancellationToken);
            await ProcessRunner.RunAsync("git", ["add", "-A"], work, TimeSpan.FromSeconds(30), cancellationToken);
            await ProcessRunner.RunAsync("git", ["-c", "user.email=eval@dotnet-diag", "-c", "user.name=eval", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "scenario"], work, TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git not installed: the workspace still works, just without branch/modified-file information.
        }
    }

    private static void CopyScenario(string source, string destination)
    {
        // Shared, hermetic NuGet sources (eval/scenarios/nuget.config): the developer's private feeds must not
        // change restore results or require credentials.
        var sharedNuGetConfig = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "nuget.config");
        if (File.Exists(sharedNuGetConfig))
        {
            System.IO.Directory.CreateDirectory(destination);
            File.Copy(sharedNuGetConfig, Path.Combine(destination, "nuget.config"), overwrite: true);
        }

        foreach (var file in System.IO.Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative == "scenario.json" || relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string LastLines(string text, int count) =>
        string.Join(" / ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(count));

    private static void TryDelete(string directory)
    {
        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
