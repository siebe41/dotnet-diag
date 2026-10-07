using DotnetDiag.Core.Models;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Logging.StructuredLogger;

namespace DotnetDiag.Core.Parsing;

/// <summary>Reads errors, warnings and the overall result from an MSBuild binary log (<c>-bl</c>).</summary>
public static class BinaryLogParser
{
    public static ParsedLog Parse(string path)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var seen = new HashSet<(string, DiagnosticSeverity, string, string?, int?, int?)>();
        var projects = new List<string>();
        var projectSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool? succeeded = null;

        void AddProject(string? project)
        {
            if (!string.IsNullOrEmpty(project) && projectSet.Add(project))
            {
                projects.Add(project);
            }
        }

        void Add(BuildDiagnostic diagnostic)
        {
            if (seen.Add(diagnostic.DedupKey))
            {
                diagnostics.Add(diagnostic);
            }

            AddProject(diagnostic.Project);
        }

        // Own the stream so the file is released as soon as parsing ends. BinLogReader.ReadRecords(string)
        // leaves the handle open, which keeps the binlog locked on Windows.
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LogParseException($"'{path}' could not be opened: {ex.Message}", ex);
        }

        using var _ = stream;
        IEnumerable<Record> records;
        try
        {
            records = new BinLogReader().ReadRecords(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new LogParseException($"'{path}' could not be read as an MSBuild binary log: {ex.Message}", ex);
        }

        try
        {
            foreach (var record in records)
            {
                switch (record.Args)
                {
                    case BuildErrorEventArgs e:
                        Add(new BuildDiagnostic(
                            e.Code ?? "ERROR",
                            DiagnosticSeverity.Error,
                            e.Message ?? string.Empty,
                            NullIfEmpty(e.File),
                            e.LineNumber > 0 ? e.LineNumber : null,
                            e.ColumnNumber > 0 ? e.ColumnNumber : null,
                            NullIfEmpty(e.ProjectFile)));
                        break;

                    case BuildWarningEventArgs w:
                        Add(new BuildDiagnostic(
                            w.Code ?? "WARNING",
                            DiagnosticSeverity.Warning,
                            w.Message ?? string.Empty,
                            NullIfEmpty(w.File),
                            w.LineNumber > 0 ? w.LineNumber : null,
                            w.ColumnNumber > 0 ? w.ColumnNumber : null,
                            NullIfEmpty(w.ProjectFile)));
                        break;

                    case ProjectStartedEventArgs p:
                        AddProject(p.ProjectFile);
                        break;

                    case BuildFinishedEventArgs f:
                        succeeded = f.Succeeded;
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not LogParseException)
        {
            if (diagnostics.Count == 0)
            {
                throw new LogParseException($"'{path}' is not a readable MSBuild binary log: {ex.Message}", ex);
            }

            // A truncated binlog (e.g. the build was killed) still yields useful diagnostics.
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            succeeded = false;
        }

        return new ParsedLog
        {
            Kind = InputKind.BinaryLog,
            SourceName = Path.GetFileName(path),
            Diagnostics = diagnostics,
            BuildSucceeded = succeeded,
            Projects = projects.Where(p => !p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)).ToArray(),
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
