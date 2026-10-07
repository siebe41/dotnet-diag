using System.CommandLine;
using System.Text.Json;
using DotnetDiag.Cli.Commands;

namespace DotnetDiag.Tests.Cli;

/// <summary>End-to-end tests of the command tree; Console is redirected, so these never run in parallel.</summary>
[Collection(nameof(ConsoleCollection))]
public class CommandLineTests
{
    [Fact]
    public async Task Analyze_json_with_exit_code_reports_problems()
    {
        var (exit, stdout, _) = await RunAsync("analyze", Fixtures.Path("missing-package-cs0246.txt"), "--format", "json", "--offline", "--exit-code");

        Assert.Equal(ExitCodes.ProblemsFound, exit);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal(5, json.RootElement.GetProperty("errorCount").GetInt32());
        Assert.Equal("none", json.RootElement.GetProperty("ai").GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Analyze_markdown_succeeds_without_exit_code_flag()
    {
        var (exit, stdout, _) = await RunAsync("analyze", Fixtures.Path("dotnet-test-vstest.txt"), "-f", "markdown", "--offline");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("## Failing tests", stdout);
    }

    [Fact]
    public async Task Analyze_missing_file_is_an_input_error()
    {
        var (exit, _, stderr) = await RunAsync("analyze", "does-not-exist.binlog", "--offline");

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("File not found", stderr);
    }

    [Fact]
    public async Task Analyze_rejects_unknown_provider()
    {
        var (exit, _, stderr) = await RunAsync("analyze", Fixtures.Path("syntax-errors.txt"), "--provider", "skynet");

        Assert.NotEqual(0, exit);
        Assert.Contains("skynet", stderr);
    }

    [Fact]
    public async Task Explain_prints_catalog_entry()
    {
        var (exit, stdout, _) = await RunAsync("explain", "nu1605");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Package downgrade detected", stdout);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        var root = new RootCommand { AnalyzeCommand.Create(), ExplainCommand.Create(), DoctorCommand.Create() };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Spectre.Console.AnsiConsole.Console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
        {
            Out = new Spectre.Console.AnsiConsoleOutput(stdout),
            Ansi = Spectre.Console.AnsiSupport.No,
            Interactive = Spectre.Console.InteractionSupport.No,
        });
        Spectre.Console.AnsiConsole.Console.Profile.Width = 120;
        try
        {
            var config = new InvocationConfiguration { Output = stdout, Error = stderr };
            var exit = await root.Parse(args).InvokeAsync(config);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public sealed class ConsoleCollection;
