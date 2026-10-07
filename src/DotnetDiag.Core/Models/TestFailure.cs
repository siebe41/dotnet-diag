namespace DotnetDiag.Core.Models;

/// <summary>A failing test as reported by <c>dotnet test</c> (VSTest or Microsoft.Testing.Platform output).</summary>
public sealed record TestFailure(
    string TestName,
    string ErrorMessage,
    IReadOnlyList<StackFrameInfo> StackTrace,
    string? Duration = null)
{
    /// <summary>The first frame that points at user code (not the BCL or a test framework).</summary>
    public StackFrameInfo? FirstUserFrame =>
        StackTrace.FirstOrDefault(f => f.IsUserCode && f.File is not null)
        ?? StackTrace.FirstOrDefault(f => f.IsUserCode);

    /// <summary>Exception type at the start of the message, e.g. <c>System.NullReferenceException</c>.</summary>
    public string? ExceptionType
    {
        get
        {
            var match = Parsing.Patterns.ExceptionPrefix().Match(ErrorMessage);
            return match.Success ? match.Groups["type"].Value : null;
        }
    }
}

public sealed record StackFrameInfo(string Method, string? File = null, int? Line = null)
{
    private static readonly string[] FrameworkPrefixes =
    [
        "System.", "Microsoft.", "Xunit.", "NUnit.", "Moq.", "Castle.", "FluentAssertions.",
        "Shouldly.", "NSubstitute.", "lambda_method", "Internal.", "Polly.",
    ];

    public bool IsUserCode => !FrameworkPrefixes.Any(p => Method.StartsWith(p, StringComparison.Ordinal));

    public override string ToString() =>
        File is null ? Method : Line is { } l ? $"{Method} in {File}:{l}" : $"{Method} in {File}";
}

public sealed record TestRunSummary(int Total, int Passed, int Failed, int Skipped)
{
    public static TestRunSummary operator +(TestRunSummary a, TestRunSummary b) =>
        new(a.Total + b.Total, a.Passed + b.Passed, a.Failed + b.Failed, a.Skipped + b.Skipped);
}
