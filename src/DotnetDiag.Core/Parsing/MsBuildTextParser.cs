using DotnetDiag.Core.Models;

namespace DotnetDiag.Core.Parsing;

/// <summary>
/// Extracts diagnostics from textual MSBuild output: <c>dotnet build</c> console output (classic or
/// terminal logger), <c>-flp</c> file logs and CI logs with timestamps/annotations.
/// </summary>
public static class MsBuildTextParser
{
    private static readonly string[] ToolOrigins = ["CSC", "VBC", "FSC", "MSBUILD", "EXEC", "NUGET", "SGEN", "AL", "RESGEN"];

    public sealed record Result(IReadOnlyList<BuildDiagnostic> Diagnostics, bool? BuildSucceeded, IReadOnlyList<string> Projects);

    public static Result Parse(IEnumerable<string> lines)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var seen = new HashSet<(string, DiagnosticSeverity, string, string?, int?, int?)>();
        var projects = new List<string>();
        var projectSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool? succeeded = null;
        BuildDiagnostic? previous = null;
        var previousWasDuplicate = false;

        foreach (var raw in lines)
        {
            var line = TextSanitizer.CleanLine(raw);
            if (line.Length == 0)
            {
                continue;
            }

            if (Patterns.BuildFailed().IsMatch(line))
            {
                succeeded = false;
            }
            else if (succeeded is null && Patterns.BuildSucceeded().IsMatch(line))
            {
                succeeded = true;
            }

            if (TryParseLine(line, out var diagnostic))
            {
                // NuGet repeats "error NU1605:" for each line of a dependency chain ("App -> A 9.0.0 -> B (>= 9.0.0)").
                if (previous is not null && IsContinuation(previous, diagnostic))
                {
                    if (!previousWasDuplicate)
                    {
                        var merged = previous with { Message = previous.Message + "\n" + diagnostic.Message };
                        diagnostics[^1] = merged;
                        seen.Add(merged.DedupKey);
                        previous = merged;
                    }

                    continue;
                }

                previousWasDuplicate = !seen.Add(diagnostic.DedupKey);
                if (!previousWasDuplicate)
                {
                    diagnostics.Add(diagnostic);
                }

                previous = diagnostic;

                if (diagnostic.Project is { } project && projectSet.Add(project))
                {
                    projects.Add(project);
                }
            }
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            succeeded = false;
        }

        return new Result(diagnostics, succeeded, projects);
    }

    public static bool TryParseLine(string line, out BuildDiagnostic diagnostic)
    {
        diagnostic = null!;
        if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0 &&
            line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var match = Patterns.MsBuildDiagnostic().Match(line.Trim());
        if (!match.Success)
        {
            return false;
        }

        var origin = match.Groups["origin"].Success ? match.Groups["origin"].Value.Trim() : null;
        string? file = null;
        int? lineNumber = null;
        int? column = null;
        string? project = null;
        string? tfm = null;

        if (!string.IsNullOrEmpty(origin) && !ToolOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            file = origin;
        }

        if (match.Groups["pos"].Success)
        {
            var parts = match.Groups["pos"].Value.Split(',');
            lineNumber = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Length > 1)
            {
                column = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (match.Groups["proj"].Success)
        {
            var value = match.Groups["proj"].Value;
            var separator = value.IndexOf("::", StringComparison.Ordinal);
            if (separator >= 0)
            {
                var properties = value[(separator + 2)..];
                const string tfmKey = "TargetFramework=";
                var tfmIndex = properties.IndexOf(tfmKey, StringComparison.OrdinalIgnoreCase);
                if (tfmIndex >= 0)
                {
                    tfm = properties[(tfmIndex + tfmKey.Length)..].Split(';', ']')[0];
                }

                value = value[..separator];
            }

            project = value;
        }

        // NuGet and SDK diagnostics are reported against the project file itself; the bracketed suffix is
        // then usually the solution being built, so the origin is the more precise project.
        if (file is not null && IsProjectFile(file) && !IsSolutionFile(file))
        {
            project = file;
        }

        var severity = match.Groups["sev"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;

        diagnostic = new BuildDiagnostic(
            Code: match.Groups["code"].Value.ToUpperInvariant() switch
            {
                var c when c.StartsWith("XUNIT", StringComparison.Ordinal) => "xUnit" + c[5..],
                var c => c,
            },
            Severity: severity,
            Message: match.Groups["msg"].Value.Trim(),
            File: file,
            Line: lineNumber,
            Column: column,
            Project: project,
            TargetFramework: tfm);
        return true;
    }

    private static bool IsContinuation(BuildDiagnostic previous, BuildDiagnostic current) =>
        current.Code == previous.Code &&
        current.File == previous.File &&
        current.Category == DiagnosticCategory.NuGet &&
        current.Message.Contains(" -> ", StringComparison.Ordinal) &&
        !current.Message.Contains("Detected", StringComparison.Ordinal);

    internal static bool IsSolutionFile(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    internal static bool IsProjectFile(string path) =>
        path.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
}
