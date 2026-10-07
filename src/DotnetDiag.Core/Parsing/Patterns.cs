using System.Text.RegularExpressions;

namespace DotnetDiag.Core.Parsing;

/// <summary>Source-generated regular expressions shared by the parsers.</summary>
internal static partial class Patterns
{
    /// <summary>
    /// Canonical MSBuild diagnostic: <c>origin[(pos)] : [subcategory] error|warning CODE: message [project]</c>.
    /// The origin may be a file path (Windows or Unix), a tool name (CSC, MSBUILD) or a project file.
    /// </summary>
    [GeneratedRegex(
        @"^(?:(?<origin>.+?)(?:\((?<pos>\d+(?:,\d+){0,3})\))?\s*:\s+)?(?:(?<subcat>[A-Za-z][A-Za-z ]*?)\s+)?(?<sev>error|warning)\s+(?<code>[A-Za-z]{1,12}\d{1,6})\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\[\]]+?\.(?:[a-z]{2,6}proj|slnx?|proj)(?:::[^\]]*)?)\])?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex MsBuildDiagnostic();

    /// <summary>ANSI/VT100 escape sequences (colors, cursor movement, OSC hyperlinks).</summary>
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])")]
    public static partial Regex AnsiEscape();

    /// <summary>Leading CI timestamps such as <c>2026-01-02T10:11:12.1234567Z </c> or <c>[10:11:12] </c>.</summary>
    [GeneratedRegex(@"^(?:\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?\s+|\[\d{2}:\d{2}:\d{2}(?:\.\d+)?\]\s+)")]
    public static partial Regex LeadingTimestamp();

    /// <summary>Azure DevOps / GitHub Actions logging commands such as <c>##[error]</c> or <c>::error file=...::</c>.</summary>
    [GeneratedRegex(@"^(?:##\[(?:error|warning|section|group|endgroup|command|debug)\]|::(?:error|warning)[^:]*::)", RegexOptions.IgnoreCase)]
    public static partial Regex CiAnnotation();

    /// <summary>VSTest: <c>  Failed Ns.Class.Method [12 ms]</c>; MTP: <c>failed Method (12ms)</c>.</summary>
    [GeneratedRegex(@"^\s*(?:Failed|failed)\s+(?<name>\S.*?)\s+(?:\[(?<dur>[^\[\]]*\d[^\[\]]*)\]|\((?<dur>\d[\d.,]*\s*(?:ms|s|m|h|min)[^)]*)\))\s*$")]
    public static partial Regex FailedTestHeader();

    /// <summary>VSTest passed/skipped lines, which terminate the previous failure block.</summary>
    [GeneratedRegex(@"^\s*(?:Passed|Skipped|passed|skipped)\s+\S.*?\s+(?:\[[^\]]*\]|\([^)]*\))\s*$")]
    public static partial Regex OtherTestResultHeader();

    [GeneratedRegex(@"^\s*at\s+(?<method>.+?)(?:\s+in\s+(?<file>.+?):(?:line\s+)?(?<line>\d+))?\s*$")]
    public static partial Regex StackFrame();

    /// <summary><c>Failed!  - Failed: 1, Passed: 4, Skipped: 0, Total: 5, Duration: ...</c></summary>
    [GeneratedRegex(@"(?:Failed|Passed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)", RegexOptions.IgnoreCase)]
    public static partial Regex VsTestSummary();

    /// <summary>Microsoft.Testing.Platform summary rows (<c>total: 5</c>, <c>failed: 1</c>, ...).</summary>
    [GeneratedRegex(@"^\s*(?<key>total|failed|succeeded|skipped):\s*(?<value>\d+)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex MtpSummaryRow();

    [GeneratedRegex(@"^\s*(?<type>[A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_`]*)*(?:Exception|Error))\s*:")]
    public static partial Regex ExceptionPrefix();

    /// <summary>Terminal-logger and classic MSBuild result lines.</summary>
    [GeneratedRegex(@"^\s*(?:Build FAILED\.|Build failed with \d+ error|Build failed\b|.*\bfailed with \d+ error\(s\))", RegexOptions.IgnoreCase)]
    public static partial Regex BuildFailed();

    [GeneratedRegex(@"^\s*(?:Build succeeded\.?|Build succeeded in|Build succeeded with)", RegexOptions.IgnoreCase)]
    public static partial Regex BuildSucceeded();

    /// <summary>Single-quoted identifiers in compiler messages: <c>'Newtonsoft'</c>.</summary>
    [GeneratedRegex(@"'(?<name>[^']+)'")]
    public static partial Regex QuotedName();

    /// <summary>Double-quoted identifiers in MSBuild messages: <c>"System.Text.Json"</c>.</summary>
    [GeneratedRegex("\"(?<name>[^\"]+)\"")]
    public static partial Regex DoubleQuotedName();
}
