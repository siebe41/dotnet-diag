using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetDiag.Core.Reporting;

/// <summary>Machine-readable report for CI pipelines and editor integrations.</summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(DiagnosisResult result)
    {
        var log = result.Log;
        string Rel(string p) => result.Workspace?.ToRelative(p) ?? p.Replace('\\', '/');

        var report = new
        {
            source = log.SourceName,
            kind = log.Kind,
            buildSucceeded = log.BuildSucceeded,
            errorCount = log.ErrorCount,
            warningCount = log.WarningCount,
            tests = log.TestSummary,
            diagnostics = log.Diagnostics.Select(d => new
            {
                d.Code,
                d.Severity,
                category = d.Category,
                d.Message,
                file = d.File is null ? null : Rel(d.File),
                d.Line,
                d.Column,
                project = d.Project is null ? null : Rel(d.Project),
            }),
            testFailures = log.TestFailures.Select(f => new
            {
                name = f.TestName,
                message = f.ErrorMessage,
                exceptionType = f.ExceptionType,
                location = f.FirstUserFrame is { File: not null } fr ? $"{Rel(fr.File!)}:{fr.Line}" : null,
            }),
            hints = result.Analysis.Hints,
            missingPackages = result.Analysis.MissingPackages.Select(m => new { m.Package, m.Reason, m.Project, m.SuggestedCommand }),
            versionConflicts = result.Analysis.VersionConflicts.Select(c => new { c.Name, c.Code, c.Versions, c.RecommendedVersion, c.SuggestedCommand }),
            context = new
            {
                result.Bundle.EstimatedTokens,
                result.Bundle.TokenBudget,
                result.Bundle.Truncated,
            },
            ai = new
            {
                provider = result.Provider.ToString().ToLowerInvariant(),
                model = result.Model,
                diagnosis = result.AiResponse?.Text,
                inputTokens = result.AiResponse?.InputTokens,
                outputTokens = result.AiResponse?.OutputTokens,
                error = result.AiError,
                skipped = result.AiSkippedReason,
            },
            elapsedMs = (long)result.Elapsed.TotalMilliseconds,
        };

        return JsonSerializer.Serialize(report, Options);
    }
}
