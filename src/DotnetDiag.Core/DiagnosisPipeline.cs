using System.Diagnostics;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Core;

public enum PipelineStage
{
    Parsing,
    InspectingWorkspace,
    Analyzing,
    BuildingContext,
    QueryingModel,
}

public sealed record DiagnosisResult(
    ParsedLog Log,
    LocalAnalysis Analysis,
    DiagnosticBundle Bundle,
    LlmPrompt Prompt,
    GitWorkspace? Workspace,
    ProviderKind Provider,
    string? Model,
    LlmResponse? AiResponse,
    string? AiError,
    string? AiSkippedReason,
    TimeSpan Elapsed);

/// <summary>parse → inspect working tree → local hints → budgeted context → (optional) LLM.</summary>
public sealed class DiagnosisPipeline(ILlmProvider? provider, ContextBuilderOptions? contextOptions = null)
{
    public Task<DiagnosisResult> AnalyzeFileAsync(string path, GitWorkspace? workspace, IProgress<PipelineStage>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(() => LogParser.ParseFile(path), workspace, progress, cancellationToken);

    public Task<DiagnosisResult> AnalyzeTextAsync(string text, string sourceName, GitWorkspace? workspace, IProgress<PipelineStage>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(() => LogParser.ParseText(text, sourceName), workspace, progress, cancellationToken);

    public async Task<DiagnosisResult> RunAsync(Func<ParsedLog> parse, GitWorkspace? workspace, IProgress<PipelineStage>? progress = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        progress?.Report(PipelineStage.Parsing);
        var log = parse();

        progress?.Report(PipelineStage.InspectingWorkspace);
        _ = workspace?.ModifiedFiles; // warm the git status cache while the spinner shows this stage

        progress?.Report(PipelineStage.Analyzing);
        var analysis = new LocalDiagnosticEngine(workspace).Analyze(log);

        progress?.Report(PipelineStage.BuildingContext);
        var bundle = new ContextBuilder(contextOptions).Build(log, analysis, workspace);
        var prompt = PromptBuilder.Build(bundle, analysis);

        LlmResponse? response = null;
        string? error = null;
        string? skipped = null;
        if (provider is null)
        {
            skipped = "No AI provider configured.";
        }
        else if (!log.HasProblems)
        {
            skipped = "No errors or failing tests found; nothing to diagnose.";
        }
        else
        {
            progress?.Report(PipelineStage.QueryingModel);
            try
            {
                response = await provider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (LlmProviderException ex)
            {
                error = ex.Message;
            }
        }

        return new DiagnosisResult(
            log,
            analysis,
            bundle,
            prompt,
            workspace,
            provider?.Kind ?? ProviderKind.None,
            provider?.Model,
            response,
            error,
            skipped,
            stopwatch.Elapsed);
    }
}
