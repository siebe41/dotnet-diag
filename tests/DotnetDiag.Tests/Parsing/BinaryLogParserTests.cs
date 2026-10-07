using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Logging.StructuredLogger;

namespace DotnetDiag.Tests.Parsing;

public sealed class BinaryLogParserTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnet-diag-binlog-" + Guid.NewGuid().ToString("N"));

    public BinaryLogParserTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Reads_errors_warnings_and_result_from_a_binary_log()
    {
        var path = WriteBinlog(
            new BuildErrorEventArgs(null, "CS0246", "/repo/src/App/Program.cs", 3, 7, 0, 0,
                "The type or namespace name 'Newtonsoft' could not be found (are you missing a using directive or an assembly reference?)",
                null, "Csc") { ProjectFile = "/repo/src/App/App.csproj" },
            new BuildWarningEventArgs(null, "NU1603", "/repo/src/App/App.csproj", 0, 0, 0, 0,
                "App depends on Serilog (>= 3.0.0) but Serilog 3.0.0 was not found. An approximate best match of Serilog 3.0.1 was resolved.",
                null, "NuGet") { ProjectFile = "/repo/src/App/App.csproj" });

        Assert.True(LogParser.IsBinaryLog(path));
        var log = LogParser.ParseFile(path);

        Assert.Equal(InputKind.BinaryLog, log.Kind);
        Assert.False(log.BuildSucceeded);
        var error = Assert.Single(log.Errors);
        Assert.Equal("CS0246", error.Code);
        Assert.Equal("/repo/src/App/Program.cs", error.File);
        Assert.Equal(3, error.Line);
        Assert.Equal(7, error.Column);
        Assert.Equal("/repo/src/App/App.csproj", error.Project);
        var warning = Assert.Single(log.Warnings);
        Assert.Equal("NU1603", warning.Code);
        Assert.Null(warning.Line);
        Assert.Contains("/repo/src/App/App.csproj", log.Projects);
    }

    [Fact]
    public void Gzip_file_that_is_not_a_binlog_reports_a_parse_error()
    {
        var path = Path.Combine(_directory, "broken.binlog");
        using (var stream = File.Create(path))
        using (var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest))
        {
            gzip.Write("definitely not a binlog"u8);
        }

        Assert.Throws<LogParseException>(() => LogParser.ParseFile(path));
    }

    [Fact]
    public void Missing_file_reports_a_parse_error()
    {
        Assert.Throws<LogParseException>(() => LogParser.ParseFile(Path.Combine(_directory, "nope.binlog")));
    }

    private string WriteBinlog(params BuildEventArgs[] diagnostics)
    {
        var path = Path.Combine(_directory, "msbuild.binlog");
        var source = new EventArgsDispatcher();
        var logger = new BinaryLogger { Parameters = path };
        logger.Initialize(source);

        var context = new BuildEventContext(1, 1, 1, 1);
        source.Dispatch(new BuildStartedEventArgs("Build started.", "") { BuildEventContext = BuildEventContext.Invalid });
        source.Dispatch(new ProjectStartedEventArgs(1, "Project started", "", "/repo/src/App/App.csproj", "Build", Array.Empty<object>(), Array.Empty<object>(), BuildEventContext.Invalid) { BuildEventContext = context });
        foreach (var e in diagnostics)
        {
            e.BuildEventContext = context;
            source.Dispatch(e);
        }

        source.Dispatch(new BuildFinishedEventArgs("Build FAILED.", "", succeeded: false) { BuildEventContext = BuildEventContext.Invalid });
        logger.Shutdown();
        return path;
    }
}
