using System.Text;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Tests.Context;

public class ContextBuilderTests
{
    public static TheoryData<string> AllFixtures => new(Fixtures.TextLogs);

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Bundle_never_exceeds_the_default_1000_token_budget(string fixture)
    {
        using var repo = new TempRepo();
        var log = Fixtures.Parse(fixture);
        var analysis = new LocalDiagnosticEngine(repo.Workspace).Analyze(log);

        var bundle = new ContextBuilder().Build(log, analysis, repo.Workspace);

        Assert.Equal(1000, bundle.TokenBudget);
        Assert.InRange(bundle.EstimatedTokens, 1, 1000);
        Assert.Equal(TokenEstimator.Estimate(bundle.Text), bundle.EstimatedTokens);
        Assert.DoesNotContain('\u001b', bundle.Text);
    }

    [Fact]
    public void Resolves_ci_paths_into_the_local_working_tree_and_includes_source()
    {
        using var repo = new TempRepo();
        var log = Fixtures.Parse("missing-package-cs0246.txt");
        var bundle = new ContextBuilder().Build(log, new LocalDiagnosticEngine(repo.Workspace).Analyze(log), repo.Workspace);

        Assert.Contains("- CS0246 src/Shop.Api/Services/OrderSerializer.cs:3:7: The type or namespace name 'Newtonsoft'", bundle.Text);
        Assert.Contains("CS0103 src/Shop.Api/Services/OrderSerializer.cs:14:20 (+1 more: src/Shop.Api/Services/OrderSerializer.cs:20:16)", bundle.Text);
        Assert.DoesNotContain("/home/runner/work", bundle.Text);
        Assert.Contains("repo: branch main", bundle.Text);

        Assert.Contains("## Source", bundle.Text);
        Assert.Contains(">3 | using Newtonsoft.Json;", bundle.Text);
        Assert.Contains(bundle.Snippets, s => s.RelativePath == "src/Shop.Api/Program.cs" && s.TargetLine == 8);

        Assert.Contains("## Package references", bundle.Text);
        Assert.Contains("src/Shop.Api/Shop.Api.csproj: Serilog.AspNetCore 8.0.3, Microsoft.Extensions.Http 9.0.0", bundle.Text);
        Assert.Contains("## Warnings (top codes)\nCS8618 x1", bundle.Text);
        Assert.False(bundle.Truncated);
    }

    [Fact]
    public void Root_cause_categories_come_before_compiler_errors_and_symptoms()
    {
        var log = Fixtures.Parse("missing-package-cs0246.txt");
        var bundle = new ContextBuilder().Build(log, new LocalDiagnosticEngine().Analyze(log));

        Assert.True(bundle.Text.IndexOf("- CS0246", StringComparison.Ordinal) < bundle.Text.IndexOf("- CS0006", StringComparison.Ordinal));
    }

    [Fact]
    public void Large_logs_are_truncated_with_an_omission_note()
    {
        var text = new StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            text.AppendLine($"/repo/src/Gen/File{i}.cs({i + 1},5): error CS0103: The name 'value{i}' does not exist in the current context [/repo/src/Gen/Gen.csproj]");
        }

        text.AppendLine("/repo/src/Gen/Gen.csproj : error NU1101: Unable to find package Missing.Pkg. No packages exist with this id in source(s): nuget.org");
        var log = LogParser.ParseText(text.ToString());
        var bundle = new ContextBuilder().Build(log, new LocalDiagnosticEngine().Analyze(log));

        Assert.True(bundle.Truncated);
        Assert.InRange(bundle.EstimatedTokens, 500, 1000);
        Assert.Equal(401, bundle.TotalErrorGroups);
        Assert.Matches(@"\(\+\d+ more error group\(s\) omitted to fit the 1000-token budget\)", bundle.Text);
        // The restore error is the likely root cause, so it is kept even though it was logged last.
        Assert.Contains("- NU1101", bundle.Text);
        Assert.True(bundle.Text.IndexOf("- NU1101", StringComparison.Ordinal) < bundle.Text.IndexOf("- CS0103", StringComparison.Ordinal));
    }

    [Fact]
    public void Smaller_budgets_are_respected()
    {
        var log = Fixtures.Parse("dotnet-test-vstest.txt");
        var bundle = new ContextBuilder(new ContextBuilderOptions { MaxTokens = 200 }).Build(log, new LocalDiagnosticEngine().Analyze(log));

        Assert.InRange(bundle.EstimatedTokens, 1, 200);
        Assert.True(bundle.IncludedTestFailures >= 1);
    }

    [Fact]
    public void Test_failures_include_message_and_user_frames_only()
    {
        var log = Fixtures.Parse("dotnet-test-vstest.txt");
        var bundle = new ContextBuilder().Build(log, new LocalDiagnosticEngine().Analyze(log));

        Assert.Contains("## Failing tests (3)", bundle.Text);
        Assert.Contains("### Calculator.Tests.MathEngineTests.Divide_ByTwo_ReturnsHalf", bundle.Text);
        Assert.Contains("Assert.Equal() Failure: Values differ / Expected: 2.5 / Actual:   2", bundle.Text);
        Assert.Contains("  at Calculator.History.Undo() in /src/calc/src/Calculator/History.cs:34", bundle.Text);
        Assert.DoesNotContain("System.RuntimeMethodHandle", bundle.Text);
        Assert.Contains("tests: 3 failed, 24 passed, 1 skipped of 28", bundle.Text);
    }
}
