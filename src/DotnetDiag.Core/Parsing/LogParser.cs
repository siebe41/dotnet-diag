using DotnetDiag.Core.Models;

namespace DotnetDiag.Core.Parsing;

/// <summary>Entry point: detects the input format and dispatches to the right parser.</summary>
public static class LogParser
{
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];

    public static ParsedLog ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new LogParseException($"File not found: {path}");
        }

        if (IsBinaryLog(path))
        {
            return BinaryLogParser.Parse(path);
        }

        return ParseText(TextDecoding.ReadFile(path), Path.GetFileName(path));
    }

    public static ParsedLog ParseText(string text, string sourceName = "stdin")
    {
        var lines = TextSanitizer.CleanLines(text).ToArray();
        var build = MsBuildTextParser.Parse(lines);
        var tests = TestOutputParser.Parse(lines);

        var projects = build.Projects
            .Where(p => !p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new ParsedLog
        {
            Kind = tests.LooksLikeTestOutput ? InputKind.TestOutput : InputKind.BuildOutput,
            SourceName = sourceName,
            Diagnostics = build.Diagnostics,
            TestFailures = tests.Failures,
            TestSummary = tests.Summary,
            BuildSucceeded = build.BuildSucceeded,
            Projects = projects,
        };
    }

    /// <summary>The most recent <c>*.binlog</c> in a folder (<c>dotnet build -bl</c> writes msbuild.binlog), or null.</summary>
    public static string? FindLatestBinaryLog(string directory) =>
        new DirectoryInfo(directory).GetFiles("*.binlog").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;

    /// <summary>Binlogs are gzip streams; check the magic bytes rather than trusting the extension.</summary>
    public static bool IsBinaryLog(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[2];
            return stream.Read(header) == 2 && header.SequenceEqual(GzipMagic);
        }
        catch (IOException)
        {
            return path.EndsWith(".binlog", StringComparison.OrdinalIgnoreCase);
        }
    }
}
