using System.Text;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Core.Context;

public sealed record LlmPrompt(string System, string User)
{
    public int EstimatedTokens => TokenEstimator.Estimate(System) + TokenEstimator.Estimate(User);
}

/// <summary>Wraps a <see cref="DiagnosticBundle"/> in instructions that steer the model toward an actionable fix.</summary>
public static class PromptBuilder
{
    public const string SystemPrompt =
        """
        You are dotnet-diag, an expert in .NET, C#, MSBuild, NuGet and dotnet test.
        You receive a compact diagnostic bundle extracted from a failing build or test run.
        Find the root cause (separate root errors from cascading ones) and propose the smallest correct fix.

        Answer in GitHub-flavored Markdown using exactly these sections:
        ## Root cause
        One to three sentences.
        ## Fix
        Numbered steps. Put shell commands (for example `dotnet add <project> package <id>`) in ```bash blocks
        and code changes as unified diffs in ```diff blocks against the file paths shown in the bundle.
        ## Why
        A short explanation.
        ## Confidence
        high, medium or low, plus what would confirm the diagnosis.

        Rules: only reference files, projects, packages and versions that appear in the bundle or are well known;
        never invent file contents you have not seen. If information is missing, say exactly what to collect
        (for example `dotnet build -bl`). Keep the answer under 350 words.
        """;

    private const int MaxHints = 5;

    public static LlmPrompt Build(DiagnosticBundle bundle, LocalAnalysis analysis)
    {
        var user = new StringBuilder();
        user.Append("Diagnose this .NET failure.\n\n");
        user.Append(bundle.Text).Append('\n');

        var hints = analysis.Hints.Where(h => !h.IsLikelySymptom).Take(MaxHints).ToList();
        if (hints.Count > 0)
        {
            user.Append("\n## Offline heuristics (may be incomplete)\n");
            foreach (var hint in hints)
            {
                user.Append("- ").Append(hint.Code).Append(": ")
                    .Append(TextSanitizer.Truncate(hint.Summary, 160)).Append('\n');
            }
        }

        return new LlmPrompt(SystemPrompt, user.ToString().TrimEnd());
    }
}
