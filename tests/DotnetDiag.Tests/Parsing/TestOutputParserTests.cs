using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Tests.Parsing;

public class TestOutputParserTests
{
    [Fact]
    public void Parses_vstest_failures_and_summary()
    {
        var log = Fixtures.Parse("dotnet-test-vstest.txt");

        Assert.Equal(InputKind.TestOutput, log.Kind);
        Assert.Equal(3, log.TestFailures.Count);
        Assert.Equal(new TestRunSummary(28, 24, 3, 1), log.TestSummary);
        Assert.Equal(0, log.ErrorCount);
        Assert.True(log.HasProblems);
    }

    [Fact]
    public void Captures_assertion_message_and_first_user_frame()
    {
        var failure = Fixtures.Parse("dotnet-test-vstest.txt").TestFailures[0];

        Assert.Equal("Calculator.Tests.MathEngineTests.Divide_ByTwo_ReturnsHalf", failure.TestName);
        Assert.Equal("8 ms", failure.Duration);
        Assert.Contains("Assert.Equal() Failure: Values differ", failure.ErrorMessage);
        Assert.Contains("Expected: 2.5", failure.ErrorMessage);
        Assert.Contains("Actual:   2", failure.ErrorMessage);
        Assert.Equal(3, failure.StackTrace.Count);

        var frame = failure.FirstUserFrame!;
        Assert.Equal("Calculator.Tests.MathEngineTests.Divide_ByTwo_ReturnsHalf()", frame.Method);
        Assert.Equal("/src/calc/tests/Calculator.Tests/MathEngineTests.cs", frame.File);
        Assert.Equal(21, frame.Line);
    }

    [Fact]
    public void Framework_frames_are_not_user_code()
    {
        var failure = Fixtures.Parse("dotnet-test-vstest.txt").TestFailures[0];

        Assert.False(failure.StackTrace[1].IsUserCode);
        Assert.StartsWith("System.RuntimeMethodHandle", failure.StackTrace[1].Method);
    }

    [Fact]
    public void Extracts_exception_type_and_production_frame()
    {
        var failure = Fixtures.Parse("dotnet-test-vstest.txt").TestFailures[1];

        Assert.Equal("System.NullReferenceException", failure.ExceptionType);
        Assert.Equal("/src/calc/src/Calculator/History.cs", failure.FirstUserFrame!.File);
        Assert.Equal(34, failure.FirstUserFrame.Line);
    }

    [Fact]
    public void Handles_parameterized_test_names_and_ignores_standard_output()
    {
        var failure = Fixtures.Parse("dotnet-test-vstest.txt").TestFailures[2];

        Assert.Equal("Calculator.Tests.ParserTests.Parse_Expression(input: \"1 + 2\", expected: 3)", failure.TestName);
        Assert.Equal("< 1 ms", failure.Duration);
        Assert.DoesNotContain("Parsing: 1 + 2", failure.ErrorMessage);
        Assert.Equal(3, failure.StackTrace.Count);
    }

    [Fact]
    public void Parses_microsoft_testing_platform_output()
    {
        var log = Fixtures.Parse("dotnet-test-mtp.txt");

        var failure = Assert.Single(log.TestFailures);
        Assert.Equal("Calculator.Tests.MathEngineTests.Divide_ByTwo_ReturnsHalf", failure.TestName);
        Assert.Contains("Expected: 2.5", failure.ErrorMessage);
        Assert.Equal(21, failure.FirstUserFrame!.Line);
        Assert.Equal("/src/calc/tests/Calculator.Tests/MathEngineTests.cs", failure.FirstUserFrame.File);
        Assert.Equal(new TestRunSummary(28, 26, 1, 1), log.TestSummary);
    }

    [Fact]
    public void Sums_summaries_from_multiple_test_projects()
    {
        var text = """
            Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5, Duration: 10 ms - A.Tests.dll (net9.0)
            Passed!  - Failed:     0, Passed:    10, Skipped:     2, Total:    12, Duration: 20 ms - B.Tests.dll (net9.0)
            """;

        var log = LogParser.ParseText(text);

        Assert.Equal(new TestRunSummary(17, 14, 1, 2), log.TestSummary);
    }
}
