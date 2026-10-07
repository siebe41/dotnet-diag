using System.CommandLine;
using DotnetDiag.Cli.Rendering;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using Spectre.Console;

namespace DotnetDiag.Cli.Commands;

internal static class ExplainCommand
{
    public static Command Create()
    {
        var code = new Argument<string?>("code")
        {
            Description = "Diagnostic code, e.g. CS0246, MSB3277, NU1605, NETSDK1045.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var list = new Option<bool>("--list")
        {
            Description = "List every code dotnet-diag knows offline.",
        };

        var command = new Command("explain", "Explain a diagnostic code offline (no log or AI needed).") { code, list };
        command.SetAction(parseResult =>
        {
            var console = AnsiConsole.Console;
            var value = parseResult.GetValue(code);

            if (parseResult.GetValue(list) || string.IsNullOrWhiteSpace(value))
            {
                var table = new Table().Border(TableBorder.Rounded).BorderStyle(Theme.Border)
                    .AddColumn("Code").AddColumn("Category").AddColumn("Title");
                foreach (var entry in KnownErrorCatalog.All.OrderBy(e => e.Code, StringComparer.Ordinal))
                {
                    var category = DiagnosticCategories.FromCode(entry.Code);
                    table.AddRow(Theme.CodeMarkup(entry.Code, category), Markup.Escape(category.DisplayName()), Markup.Escape(entry.Title));
                }

                console.Write(table);
                return ExitCodes.Success;
            }

            var known = KnownErrorCatalog.Find(value);
            if (known is null)
            {
                console.MarkupLine($"[yellow]{Markup.Escape(value.ToUpperInvariant())}[/] is not in the offline catalog.");
                console.MarkupLine($"Docs: [link]{Markup.Escape(KnownErrorCatalog.DocsUrlFor(value))}[/]");
                return ExitCodes.Success;
            }

            console.Write(ConsoleReportRenderer.KnownErrorPanel(known));
            return ExitCodes.Success;
        });

        return command;
    }
}
