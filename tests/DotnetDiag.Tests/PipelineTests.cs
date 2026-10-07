using System.Text.Json;
using DotnetDiag.Core;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Reporting;

namespace DotnetDiag.Tests;

public class PipelineTests
{
    [Fact]
    public async Task Without_a_provider_returns_local_analysis_only()
    {
        var stages = new List<PipelineStage>();
        var result = await new DiagnosisPipeline(provider: null)
            .AnalyzeFileAsync(Fixtures.Path("nuget-restore.txt"), workspace: null, new SyncProgress(stages));

        Assert.Null(result.AiResponse);
        Assert.Equal("No AI provider configured.", result.AiSkippedReason);
        Assert.NotEmpty(result.Analysis.Hints);
        Assert.Equal([PipelineStage.Parsing, PipelineStage.InspectingWorkspace, PipelineStage.Analyzing, PipelineStage.BuildingContext], stages);
    }

    [Fact]
    public async Task Sends_the_budgeted_prompt_to_the_provider()
    {
        var provider = new StubProvider("## Root cause\nNewtonsoft.Json is not referenced.");
        var result = await new DiagnosisPipeline(provider).AnalyzeFileAsync(Fixtures.Path("missing-package-cs0246.txt"), workspace: null);

        Assert.Equal("## Root cause\nNewtonsoft.Json is not referenced.", result.AiResponse!.Text);
        Assert.Same(result.Prompt, provider.LastPrompt);
        Assert.Equal(ProviderKind.Ollama, result.Provider);
    }

    [Fact]
    public async Task Provider_failures_degrade_to_local_hints()
    {
        var result = await new DiagnosisPipeline(new StubProvider(error: "connection refused"))
            .AnalyzeFileAsync(Fixtures.Path("syntax-errors.txt"), workspace: null);

        Assert.Null(result.AiResponse);
        Assert.Equal("connection refused", result.AiError);
        Assert.Contains(result.Analysis.Hints, h => h.Code == "CS1002");
    }

    [Fact]
    public async Task Clean_logs_do_not_call_the_provider()
    {
        var provider = new StubProvider("unused");
        var result = await new DiagnosisPipeline(provider).AnalyzeTextAsync("Build succeeded.\n    0 Error(s)\n", "stdin", workspace: null);

        Assert.Null(provider.LastPrompt);
        Assert.Contains("nothing to diagnose", result.AiSkippedReason);
    }

    [Fact]
    public async Task Markdown_report_contains_tables_and_hints()
    {
        var result = await new DiagnosisPipeline(null).AnalyzeFileAsync(Fixtures.Path("nuget-restore.txt"), workspace: null);

        var markdown = MarkdownReportWriter.Write(result);

        Assert.StartsWith("# dotnet-diag: nuget-restore.txt", markdown);
        Assert.Contains("| `NU1101` | 1 |", markdown);
        Assert.Contains("## Missing NuGet references", markdown);
        Assert.Contains("| `Contoso.Payments.Sdk` | not found in NuGet feeds |", markdown);
        Assert.Contains("## Version conflicts", markdown);
        Assert.Contains("## Local diagnosis", markdown);
        Assert.Contains("### `NETSDK1045`", markdown);
    }

    [Fact]
    public async Task Json_report_is_machine_readable()
    {
        var result = await new DiagnosisPipeline(new StubProvider("diagnosis")).AnalyzeFileAsync(Fixtures.Path("dotnet-test-vstest.txt"), workspace: null);

        using var json = JsonDocument.Parse(JsonReportWriter.Write(result));
        var root = json.RootElement;

        Assert.Equal("testOutput", root.GetProperty("kind").GetString());
        Assert.Equal(3, root.GetProperty("testFailures").GetArrayLength());
        Assert.Equal(28, root.GetProperty("tests").GetProperty("total").GetInt32());
        Assert.Equal("/src/calc/tests/Calculator.Tests/MathEngineTests.cs:21", root.GetProperty("testFailures")[0].GetProperty("location").GetString());
        Assert.Equal("diagnosis", root.GetProperty("ai").GetProperty("diagnosis").GetString());
        Assert.True(root.GetProperty("context").GetProperty("estimatedTokens").GetInt32() <= 1000);
    }

    private sealed class SyncProgress(List<PipelineStage> stages) : IProgress<PipelineStage>
    {
        public void Report(PipelineStage value) => stages.Add(value);
    }

    private sealed class StubProvider(string? text = null, string? error = null) : ILlmProvider
    {
        public LlmPrompt? LastPrompt { get; private set; }

        public ProviderKind Kind => ProviderKind.Ollama;

        public string Model => "stub";

        public Task<LlmResponse> CompleteAsync(LlmPrompt prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return error is null
                ? Task.FromResult(new LlmResponse(text!, Model))
                : Task.FromException<LlmResponse>(new LlmProviderException(error));
        }
    }
}
