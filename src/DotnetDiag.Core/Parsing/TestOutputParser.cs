using System.Text;
using DotnetDiag.Core.Models;

namespace DotnetDiag.Core.Parsing;

/// <summary>
/// Parses failing tests out of <c>dotnet test</c> console output. Supports the VSTest console logger
/// ("Failed X [12 ms] / Error Message: / Stack Trace:") and Microsoft.Testing.Platform ("failed X (12ms)").
/// </summary>
public static class TestOutputParser
{
    private const int MaxMessageLines = 40;

    public sealed record Result(IReadOnlyList<TestFailure> Failures, TestRunSummary? Summary, bool LooksLikeTestOutput);

    private enum Section
    {
        None,
        Message,
        Stack,
        Ignored,
        MtpBody,
    }

    public static Result Parse(IEnumerable<string> lines)
    {
        var failures = new List<TestFailure>();
        TestRunSummary? summary = null;
        var looksLikeTests = false;

        string? name = null;
        string? duration = null;
        var message = new StringBuilder();
        var messageLines = 0;
        var frames = new List<StackFrameInfo>();
        var section = Section.None;

        var inMtpSummary = false;
        var mtp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (name is not null)
            {
                failures.Add(new TestFailure(name, message.ToString().Trim(), frames.ToArray(), duration));
            }

            name = null;
            duration = null;
            message.Clear();
            messageLines = 0;
            frames.Clear();
            section = Section.None;
        }

        void AppendMessage(string text)
        {
            if (messageLines++ < MaxMessageLines)
            {
                message.AppendLine(text.Trim());
            }
        }

        void FlushMtpSummary()
        {
            if (mtp.Count > 0)
            {
                var run = new TestRunSummary(
                    mtp.GetValueOrDefault("total"),
                    mtp.GetValueOrDefault("succeeded"),
                    mtp.GetValueOrDefault("failed"),
                    mtp.GetValueOrDefault("skipped"));
                summary = summary is null ? run : summary + run;
                mtp.Clear();
            }

            inMtpSummary = false;
        }

        foreach (var raw in lines)
        {
            var line = TextSanitizer.CleanLine(raw);
            var trimmed = line.Trim();

            // xUnit's own console reporter duplicates everything VSTest prints; skip it.
            if (trimmed.StartsWith("[xUnit.net", StringComparison.Ordinal))
            {
                looksLikeTests = true;
                continue;
            }

            if (inMtpSummary)
            {
                var row = Patterns.MtpSummaryRow().Match(line);
                if (row.Success)
                {
                    mtp[row.Groups["key"].Value] = int.Parse(row.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    continue;
                }

                if (trimmed.StartsWith("duration:", StringComparison.OrdinalIgnoreCase) || trimmed.Length == 0)
                {
                    continue;
                }

                FlushMtpSummary();
            }

            var vsSummary = Patterns.VsTestSummary().Match(line);
            if (vsSummary.Success)
            {
                Flush();
                looksLikeTests = true;
                var run = new TestRunSummary(
                    int.Parse(vsSummary.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(vsSummary.Groups["passed"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(vsSummary.Groups["failed"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(vsSummary.Groups["skipped"].Value, System.Globalization.CultureInfo.InvariantCulture));
                summary = summary is null ? run : summary + run;
                continue;
            }

            if (trimmed.StartsWith("Test run summary:", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                looksLikeTests = true;
                inMtpSummary = true;
                continue;
            }

            if (trimmed.StartsWith("Starting test execution", StringComparison.Ordinal) ||
                trimmed.StartsWith("A total of ", StringComparison.Ordinal) ||
                trimmed.StartsWith("Test run for ", StringComparison.Ordinal) ||
                trimmed.StartsWith("Total tests:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Test Run Failed", StringComparison.Ordinal))
            {
                looksLikeTests = true;
                if (section != Section.None && !trimmed.StartsWith("Total tests:", StringComparison.Ordinal))
                {
                    Flush();
                }

                continue;
            }

            var header = Patterns.FailedTestHeader().Match(line);
            if (header.Success)
            {
                Flush();
                looksLikeTests = true;
                name = header.Groups["name"].Value.Trim();
                duration = header.Groups["dur"].Value.Trim();
                // VSTest indents headers by two spaces and announces sections; MTP puts the message right below.
                section = trimmed.StartsWith("failed ", StringComparison.Ordinal) ? Section.MtpBody : Section.None;
                continue;
            }

            if (Patterns.OtherTestResultHeader().IsMatch(line))
            {
                Flush();
                looksLikeTests = true;
                continue;
            }

            if (name is null)
            {
                continue;
            }

            switch (trimmed)
            {
                case "Error Message:":
                    section = Section.Message;
                    continue;
                case "Stack Trace:":
                    section = Section.Stack;
                    continue;
                case "Standard Output Messages:":
                case "Standard Error Messages:":
                case "Debug Trace:":
                case "Additional Information:":
                case "Output:":
                    section = Section.Ignored;
                    continue;
            }

            switch (section)
            {
                case Section.Message:
                    AppendMessage(line);
                    break;

                case Section.Stack:
                    if (trimmed.Length == 0)
                    {
                        section = Section.Ignored;
                    }
                    else if (TryParseFrame(line, out var frame))
                    {
                        frames.Add(frame);
                    }
                    else if (trimmed.StartsWith("---", StringComparison.Ordinal))
                    {
                        // "--- End of stack trace from previous location ---"
                    }
                    else
                    {
                        // Inner exception text inside a stack trace.
                        AppendMessage(line);
                    }

                    break;

                case Section.MtpBody:
                    if (TryParseFrame(line, out var mtpFrame))
                    {
                        frames.Add(mtpFrame);
                    }
                    else if (trimmed.Length > 0 && !trimmed.StartsWith("---", StringComparison.Ordinal))
                    {
                        if (frames.Count > 0 && !line.StartsWith("  ", StringComparison.Ordinal))
                        {
                            // Unindented text after the stack: the failure block is over.
                            Flush();
                        }
                        else
                        {
                            AppendMessage(line);
                        }
                    }

                    break;
            }
        }

        Flush();
        if (inMtpSummary)
        {
            FlushMtpSummary();
        }

        return new Result(failures, summary, looksLikeTests || failures.Count > 0 || summary is not null);
    }

    internal static bool TryParseFrame(string line, out StackFrameInfo frame)
    {
        var match = Patterns.StackFrame().Match(line);
        if (!match.Success)
        {
            frame = null!;
            return false;
        }

        frame = new StackFrameInfo(
            match.Groups["method"].Value.Trim(),
            match.Groups["file"].Success ? match.Groups["file"].Value.Trim() : null,
            match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture) : null);
        return true;
    }
}
