using DotnetDiag.Cli.Rendering;
using DotnetDiag.Core;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Providers;
using Spectre.Console;

namespace DotnetDiag.Tests.Cli;

public class ConsoleReportRendererTests
{
    public static TheoryData<string> AllFixtures => new(Fixtures.TextLogs);

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Renders_every_fixture_without_markup_errors(string fixture)
    {
        using var repo = new TempRepo();
        var result = await new DiagnosisPipeline(null).AnalyzeFileAsync(Fixtures.Path(fixture), repo.Workspace);

        var output = Render(result, new RenderOptions(ShowWarnings: true, ShowContext: true));

        Assert.Contains("dotnet-diag", output);
        Assert.Contains("LLM context", output);
    }

    [Fact]
    public async Task Shows_color_coded_tables_and_local_hints_offline()
    {
        var result = await new DiagnosisPipeline(null).AnalyzeFileAsync(Fixtures.Path("nuget-restore.txt"), workspace: null);

        var output = Render(result, new RenderOptions());

        Assert.Contains("BUILD FAILED", output);
        Assert.Contains("Diagnostics", output);
        Assert.Contains("NU1101", output);
        Assert.Contains("Missing NuGet references", output);
        Assert.Contains("Version conflicts", output);
        Assert.Contains("Local diagnosis", output);
        Assert.Contains("--provider ollama", output);
    }

    [Fact]
    public void Shows_ai_diagnosis_panel()
    {
        var log = Fixtures.Parse("syntax-errors.txt");
        var analysis = new LocalDiagnosticEngine().Analyze(log);
        var bundle = new ContextBuilder().Build(log, analysis);
        var result = new DiagnosisResult(
            log, analysis, bundle, PromptBuilder.Build(bundle, analysis), null, ProviderKind.Anthropic, "claude-opus-5-5",
            new LlmResponse("## Root cause\nA missing `;` on line 12.", "claude-opus-5-5", 400, 80, "end_turn"),
            null, null, TimeSpan.FromSeconds(2));

        var output = Render(result, new RenderOptions());

        Assert.Contains("AI diagnosis · Anthropic · claude-opus-5-5", output);
        Assert.Contains("A missing ; on line 12.", output);
        Assert.Contains("Offline checks", output);
        Assert.Contains("model usage 400 in / 80 out", output);
    }

    private static string Render(DiagnosisResult result, RenderOptions options)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = 160;
        ConsoleReportRenderer.Render(console, result, options);
        return writer.ToString();
    }
}
