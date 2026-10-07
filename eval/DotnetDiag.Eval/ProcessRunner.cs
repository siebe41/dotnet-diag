using System.Diagnostics;
using System.Text;

namespace DotnetDiag.Eval;

internal sealed record ProcessResult(int ExitCode, string Output, TimeSpan Elapsed);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        // Stable, parseable output regardless of the machine's language or terminal.
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["MSBUILDTERMINALLOGGER"] = "off";

        var output = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            lock (output)
            {
                output.AppendLine($"[eval] timed out after {timeout.TotalSeconds:0}s");
            }

            return new ProcessResult(-1, output.ToString(), stopwatch.Elapsed);
        }

        process.WaitForExit(); // flush async output handlers
        lock (output)
        {
            return new ProcessResult(process.ExitCode, output.ToString(), stopwatch.Elapsed);
        }
    }
}
