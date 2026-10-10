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
    public async Task Analyze_folder_uses_its_newest_binlog()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dotnet-diag-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var (emptyExit, _, emptyError) = await RunAsync("analyze", folder, "--offline");
            Assert.Equal(ExitCodes.InvalidInput, emptyExit);
            Assert.Contains("No .binlog files found", emptyError);

            // The format is detected by content, so text logs named .binlog are enough to show which file is picked.
            var older = Path.Combine(folder, "older.binlog");
            var newer = Path.Combine(folder, "newer.binlog");
            File.Copy(Fixtures.Path("syntax-errors.txt"), older);
            File.Copy(Fixtures.Path("nuget-restore.txt"), newer);
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

            var (exit, stdout, _) = await RunAsync("analyze", folder, "--offline", "--format", "json");

            Assert.Equal(ExitCodes.Success, exit);
            using var json = JsonDocument.Parse(stdout);
            Assert.Equal("newer.binlog", json.RootElement.GetProperty("source").GetString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("199")]
    [InlineData("100001")]
    public async Task Analyze_rejects_a_token_budget_outside_the_supported_range(string budget)
    {
        var (exit, _, stderr) = await RunAsync("analyze", Fixtures.Path("syntax-errors.txt"), "--offline", "--max-context-tokens", budget);

        Assert.Equal(ExitCodes.InvalidInput, exit);
        Assert.Contains("between 200 and 100000", stderr);
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

    [Fact]
    public async Task Doctor_hides_credentials_embedded_in_the_endpoint()
    {
        var (exit, stdout, _) = await RunAsync("doctor", "--provider", "ollama", "--endpoint", "https://alice:hunter2@ollama.internal:11434");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("https://***@ollama.internal:11434/", stdout);
        Assert.DoesNotContain("hunter2", stdout);
        Assert.DoesNotContain("alice", stdout);
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
