namespace DotnetDiag.Core.Models;

public enum InputKind
{
    BuildOutput,
    BinaryLog,
    TestOutput,
}

/// <summary>Everything dotnet-diag extracted from one input (binlog, text log or piped output).</summary>
public sealed class ParsedLog
{
    public required InputKind Kind { get; init; }

    public required string SourceName { get; init; }

    public IReadOnlyList<BuildDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyList<TestFailure> TestFailures { get; init; } = [];

    public TestRunSummary? TestSummary { get; init; }

    /// <summary>True/false when the log states the build result explicitly, null when unknown.</summary>
    public bool? BuildSucceeded { get; init; }

    /// <summary>Project files that appeared in the log (useful for package-reference lookups).</summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    public IEnumerable<BuildDiagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerable<BuildDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);

    public int ErrorCount => Errors.Count();

    public int WarningCount => Warnings.Count();

    public bool HasProblems => ErrorCount > 0 || TestFailures.Count > 0 || TestSummary?.Failed > 0 || BuildSucceeded == false;

    public string KindDisplayName => Kind switch
    {
        InputKind.BinaryLog => "MSBuild binary log",
        InputKind.TestOutput => "dotnet test output",
        _ => "build output",
    };
}
