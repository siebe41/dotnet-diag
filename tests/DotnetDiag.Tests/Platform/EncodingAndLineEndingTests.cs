using System.Text;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Tests.Platform;

/// <summary>Logs captured on Windows arrive with CRLF line endings and in several encodings.</summary>
public sealed class EncodingAndLineEndingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnet-diag-enc-" + Guid.NewGuid().ToString("N"));

    public EncodingAndLineEndingTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static TheoryData<string> AllFixtures => new(Fixtures.TextLogs);

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Crlf_logs_parse_exactly_like_lf_logs(string fixture)
    {
        var lf = Fixtures.Read(fixture).Replace("\r\n", "\n", StringComparison.Ordinal);
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        AssertSameParse(LogParser.ParseText(lf), LogParser.ParseText(crlf));
    }

    public static TheoryData<string, string> Encodings => new()
    {
        { "utf-8 (no BOM, .NET CLI / pwsh 7)", "utf8" },
        { "utf-8 with BOM (pwsh Out-File)", "utf8bom" },
        { "utf-16 LE with BOM (Windows PowerShell 5.1 '>')", "utf16le" },
        { "utf-16 BE with BOM", "utf16be" },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Files_in_any_common_encoding_parse_the_same(string description, string encoding)
    {
        var text = Fixtures.Read("dotnet-test-vstest.txt").Replace("Divide_ByTwo_ReturnsHalf", "Größe_Wird_Berechnet", StringComparison.Ordinal);
        var path = Path.Combine(_directory, encoding + ".txt");
        File.WriteAllBytes(path, encoding switch
        {
            "utf8" => new UTF8Encoding(false).GetBytes(text),
            "utf8bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf16le" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            "utf16be" => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        });

        var log = LogParser.ParseFile(path);

        Assert.False(LogParser.IsBinaryLog(path), description);
        Assert.Equal(InputKind.TestOutput, log.Kind);
        Assert.Equal("Calculator.Tests.MathEngineTests.Größe_Wird_Berechnet", log.TestFailures[0].TestName);
        Assert.Equal(new TestRunSummary(28, 24, 3, 1), log.TestSummary);
    }

    [Fact]
    public void Ansi_code_page_logs_fall_back_without_losing_ascii()
    {
        // "Größe" in Windows-1252 is not valid UTF-8.
        byte[] bytes = [.. "/src/A.cs(1,1): error CS0103: The name 'Gr"u8, 0xF6, 0xDF, .. "e' does not exist in the current context"u8];

        var text = TextDecoding.Decode(bytes);
        var diagnostic = Assert.Single(LogParser.ParseText(text).Errors);

        Assert.Equal("CS0103", diagnostic.Code);
        Assert.Contains("'Größe'", diagnostic.Message);
    }

    [Fact]
    public void Localized_compiler_messages_still_map_to_packages()
    {
        var log = LogParser.ParseText("""
            C:\src\App\Program.cs(3,7): error CS0246: Der Typ- oder Namespacename "Newtonsoft" wurde nicht gefunden (möglicherweise fehlt eine using-Direktive oder ein Assemblyverweis). [C:\src\App\App.csproj]
            C:\src\App\Program.cs(9,1): error CS0103: Le nom « JsonConvert » n'existe pas dans le contexte actuel. [C:\src\App\App.csproj]
            """);

        var analysis = new DotnetDiag.Core.Hints.LocalDiagnosticEngine().Analyze(log);

        Assert.Equal(2, log.ErrorCount);
        var missing = Assert.Single(analysis.MissingPackages);
        Assert.Equal("Newtonsoft.Json", missing.Package);
    }

    private static void AssertSameParse(ParsedLog expected, ParsedLog actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.BuildSucceeded, actual.BuildSucceeded);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.TestSummary, actual.TestSummary);
        Assert.Equal(expected.TestFailures.Count, actual.TestFailures.Count);
        for (var i = 0; i < expected.TestFailures.Count; i++)
        {
            Assert.Equal(expected.TestFailures[i].TestName, actual.TestFailures[i].TestName);
            Assert.Equal(expected.TestFailures[i].ErrorMessage, actual.TestFailures[i].ErrorMessage);
            Assert.Equal(expected.TestFailures[i].StackTrace, actual.TestFailures[i].StackTrace);
        }
    }
}
