using DotnetDiag.Core.Context;
using DotnetDiag.Core.Hints;

namespace DotnetDiag.Tests.Context;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_asks_for_actionable_markdown_sections()
    {
        Assert.Contains("## Root cause", PromptBuilder.SystemPrompt);
        Assert.Contains("## Fix", PromptBuilder.SystemPrompt);
        Assert.Contains("```diff", PromptBuilder.SystemPrompt);
        Assert.Contains("## Confidence", PromptBuilder.SystemPrompt);
    }

    [Fact]
    public void User_prompt_contains_bundle_and_root_cause_heuristics_but_not_symptoms()
    {
        var log = Fixtures.Parse("missing-package-cs0246.txt");
        var analysis = new LocalDiagnosticEngine().Analyze(log);
        var bundle = new ContextBuilder().Build(log, analysis);

        var prompt = PromptBuilder.Build(bundle, analysis);

        Assert.StartsWith("Diagnose this .NET failure.", prompt.User);
        Assert.Contains(bundle.Text, prompt.User);
        Assert.Contains("## Offline heuristics (may be incomplete)", prompt.User);
        Assert.Contains("- CS0246: 'Newtonsoft' cannot be resolved", prompt.User);
        Assert.DoesNotContain("- CS0006:", prompt.User);
        Assert.InRange(prompt.EstimatedTokens, bundle.EstimatedTokens, bundle.EstimatedTokens + 600);
    }
}
