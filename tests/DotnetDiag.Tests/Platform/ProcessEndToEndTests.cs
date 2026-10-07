using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetDiag.Tests.Platform;

/// <summary>
/// Runs the real dotnet-diag executable as a child process, so pipes, stdin encoding, console width and exit
/// codes are exercised the way each OS's shell delivers them.
/// </summary>
public partial class ProcessEndToEndTests
{
    [Fact]
    public async Task Piped_crlf_utf8_test_output_is_analyzed()
    {
        var input = Fixtures.Read("dotnet-test-vstest.txt")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal)
            .Replace("Divide_ByTwo_ReturnsHalf", "Größe_Wird_Berechnet", StringComparison.Ordinal);

        var (exit, stdout, stderr) = await RunAsync(["analyze", "--offline", "--format", "json", "--exit-code"], Encoding.UTF8.GetBytes(input));

        Assert.True(exit == 1, stderr);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("stdin", json.RootElement.GetProperty("source").GetString());
        Assert.Equal("Calculator.Tests.MathEngineTests.Größe_Wird_Berechnet", json.RootElement.GetProperty("testFailures")[0].GetProperty("name").GetString());
        Assert.Contains("Größe", stdout); // not \u-escaped
    }

    [Fact]
    public async Task Piped_utf16_output_from_windows_powershell_is_analyzed()
    {
        var input = Fixtures.Read("syntax-errors.txt");
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(input)];

        var (exit, stdout, stderr) = await RunAsync(["analyze", "-", "--offline", "--format", "json"], bytes);

        Assert.True(exit == 0, stderr);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal(3, json.RootElement.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Pretty_output_is_utf8_and_readable_when_redirected()
    {
        var (exit, stdout, _) = await RunAsync(["analyze", Fixtures.Path("missing-package-cs0246.txt"), "--offline"], null);

        var text = AnsiEscape().Replace(stdout, string.Empty);
        Assert.Equal(0, exit);
        Assert.Contains("BUILD FAILED", text);
        Assert.Contains("✖", text);
        Assert.Contains("╭", text);
        Assert.Contains("dotnet add", text);
        // Redirected output renders at a fixed width instead of collapsing.
        Assert.Contains(text.Split('\n'), line => line.TrimEnd().Length >= 100);
    }

    [Fact]
    public async Task Empty_stdin_and_missing_files_are_input_errors()
    {
        var (emptyExit, _, emptyErr) = await RunAsync(["analyze", "--offline"], []);
        var (missingExit, _, missingErr) = await RunAsync(["analyze", "nope.binlog", "--offline"], null);

        Assert.Equal(2, emptyExit);
        Assert.Contains("stdin was empty", emptyErr);
        Assert.Equal(2, missingExit);
        Assert.Contains("File not found", missingErr);
    }

    [Fact]
    public async Task Version_and_explain_run_as_a_process()
    {
        var (versionExit, version, _) = await RunAsync(["--version"], null);
        var (explainExit, explain, _) = await RunAsync(["explain", "NETSDK1045"], null);

        Assert.Equal(0, versionExit);
        Assert.Matches(@"^\d+\.\d+\.\d+", version.Trim());
        Assert.Equal(0, explainExit);
        Assert.Contains("SDK does not support the target framework", AnsiEscape().Replace(explain, string.Empty));
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(string[] args, byte[]? stdin)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "dotnet-diag.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        var psi = new ProcessStartInfo(dotnet)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // Isolate from the developer's/CI's own configuration.
        psi.Environment.Remove("COLUMNS");
        psi.Environment.Remove("DOTNET_DIAG_PROVIDER");
        psi.Environment["DOTNET_DIAG_CONFIG"] = Path.Combine(Path.GetTempPath(), "dotnet-diag-no-config.json");

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(stdin);
        }

        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\))")]
    private static partial Regex AnsiEscape();
}
