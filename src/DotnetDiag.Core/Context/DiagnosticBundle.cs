using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Core.Context;

/// <summary>The concise, budgeted text that is sent to an LLM (and can be printed with --show-context).</summary>
public sealed record DiagnosticBundle(
    string Text,
    int EstimatedTokens,
    int TokenBudget,
    int IncludedErrorGroups,
    int TotalErrorGroups,
    int IncludedTestFailures,
    int TotalTestFailures,
    IReadOnlyList<SourceSnippet> Snippets)
{
    public bool Truncated => IncludedErrorGroups < TotalErrorGroups || IncludedTestFailures < TotalTestFailures;
}

public sealed record ContextBuilderOptions
{
    /// <summary>Hard ceiling for <see cref="DiagnosticBundle.EstimatedTokens"/>.</summary>
    public int MaxTokens { get; init; } = 1000;

    public int MaxSnippets { get; init; } = 3;

    public int SnippetRadius { get; init; } = 3;

    public int MaxMessageLength { get; init; } = 220;

    public int MaxFramesPerTest { get; init; } = 3;
}
