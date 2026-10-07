using System.Globalization;
using System.Text;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Eval;

/// <summary>Generates real sample logs (binlog + console output) from the broken scenario projects.</summary>
internal static class Samples
{
    public static async Task<int> GenerateAsync(IReadOnlyList<Scenario> scenarios, string outputDirectory, ScenarioRunner runner)
    {
        Directory.CreateDirectory(outputDirectory);
        var rows = new List<(Scenario Scenario, string? Binlog, string Text, string Codes)>();
        foreach (var scenario in scenarios)
        {
            Console.Write($"{scenario.Name,-28}");
            var (build, binlog, text) = await runner.GenerateSampleAsync(scenario, outputDirectory, CancellationToken.None);

            // Read back what dotnet-diag sees in the file, so the index shows real results.
            var log = LogParser.ParseFile(binlog ?? text);
            var codes = string.Join(", ", log.Errors.Select(e => e.Code).Distinct()
                .Concat(log.TestFailures.Count > 0 ? [$"{log.TestFailures.Count} failing tests"] : []));
            Console.WriteLine($"exit {build.ExitCode,-3} {(binlog is null ? "text only  " : "binlog+text")}  {(codes.Length == 0 ? "(nothing recognized)" : codes)}");
            if (build.ExitCode == 0)
            {
                Console.Error.WriteLine($"warning: {scenario.Name} built successfully; its sample shows no failure.");
            }

            rows.Add((scenario, binlog, text, codes));
        }

        File.WriteAllText(Path.Combine(outputDirectory, "README.md"), Index(rows, outputDirectory));
        Console.WriteLine($"\nWrote {rows.Count} samples and README.md to {outputDirectory}");
        return 0;
    }

    private static string Index(List<(Scenario Scenario, string? Binlog, string Text, string Codes)> rows, string outputDirectory)
    {
        var sb = new StringBuilder();
        sb.Append("""
            # Sample error logs

            These are real failures, produced by building the intentionally broken projects in
            [`eval/scenarios/`](../eval/scenarios) with `dotnet build -bl` (or `dotnet test`). Each sample has:

            - `<name>.binlog`: the MSBuild binary log, which is the richest input
            - `<name>.txt`: the console output exactly as a developer saw it

            Pass `--repo eval/scenarios/<name>` so dotnet-diag can map the log's paths back to the source and show
            the offending lines.

            **Run all of them** (builds once, then analyzes every sample):

            ```powershell
            ./samples/run-samples.ps1                     # PowerShell (Windows PowerShell 5.1 or pwsh)
            ./samples/run-samples.ps1 -Filter package     # only some
            ./samples/run-samples.ps1 -Installed          # use the installed dotnet-diag tool
            # if script execution is disabled on this machine:
            powershell -ExecutionPolicy Bypass -File samples/run-samples.ps1
            ```

            ```bash
            ./samples/run-samples.sh                      # bash / zsh
            FILTER=package ./samples/run-samples.sh
            ```

            **Run one** (from the repository root):

            ```powershell
            dotnet run --project src/DotnetDiag.Cli -- analyze samples/missing-package.binlog --repo eval/scenarios/missing-package
            # as if piped from dotnet test (PowerShell has no '<' redirection):
            Get-Content samples/failing-tests.txt | dotnet run --project src/DotnetDiag.Cli -- analyze --repo eval/scenarios/failing-tests
            ```

            ```bash
            dotnet run --project src/DotnetDiag.Cli -- analyze samples/missing-package.binlog --repo eval/scenarios/missing-package
            dotnet run --project src/DotnetDiag.Cli -- analyze --repo eval/scenarios/failing-tests < samples/failing-tests.txt
            ```

            Regenerate them with your own SDK and OS (the logs then contain your temp-folder path):

            ```bash
            dotnet run --project eval/DotnetDiag.Eval -- --generate-samples samples
            ```

            | Sample | What is broken | Recognized | Try |
            |---|---|---|---|

            """);
        foreach (var (scenario, binlog, text, codes) in rows)
        {
            var file = binlog ?? text;
            var name = Path.GetFileName(file);
            var files = binlog is null ? $"[`{Path.GetFileName(text)}`]({Path.GetFileName(text)})" : $"[`{Path.GetFileName(binlog)}`]({Path.GetFileName(binlog)}), [`{Path.GetFileName(text)}`]({Path.GetFileName(text)})";
            var gap = scenario.KnownGap is null ? string.Empty : " 🟡 *known gap*";
            sb.Append(CultureInfo.InvariantCulture,
                $"| {files} | {scenario.Description}{gap} | {(codes.Length == 0 ? "–" : codes)} | `dotnet-diag analyze samples/{name} --repo eval/scenarios/{scenario.Name}` |\n");
        }

        return sb.ToString();
    }
}
