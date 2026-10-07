using System.CommandLine;
using System.Text;
using DotnetDiag.Cli.Commands;
using DotnetDiag.Cli.Rendering;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;
ConsoleSetup.Configure(AnsiConsole.Console);

var root = new RootCommand(
    "dotnet-diag: diagnose failing .NET builds and tests from MSBuild binary/text logs or piped `dotnet test` output.")
{
    AnalyzeCommand.Create(),
    ExplainCommand.Create(),
    DoctorCommand.Create(),
};

return await root.Parse(args).InvokeAsync();
