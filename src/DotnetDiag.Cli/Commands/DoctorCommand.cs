using System.CommandLine;
using DotnetDiag.Cli.Rendering;
using DotnetDiag.Core.Providers;
using DotnetDiag.Core.Workspace;
using Spectre.Console;

namespace DotnetDiag.Cli.Commands;

internal static class DoctorCommand
{
    public static Command Create()
    {
        var providers = new ProviderOptionSet();
        var command = new Command("doctor", "Show the resolved AI provider configuration and where each value comes from.");
        providers.AddTo(command);

        command.SetAction(parseResult =>
        {
            var console = AnsiConsole.Console;
            var settings = providers.Resolve(parseResult);
            string Origin(string key) => settings.Origins.TryGetValue(key, out var o) ? o : "-";

            var table = new Table().Border(TableBorder.Rounded).BorderStyle(Theme.Border)
                .Title("[bold]dotnet-diag configuration[/]")
                .AddColumn("Setting").AddColumn("Value").AddColumn("[grey]Source[/]");

            table.AddRow("Provider", Markup.Escape(settings.Kind.DisplayName()), Markup.Escape(Origin("Provider")));
            if (settings.Kind != ProviderKind.None)
            {
                table.AddRow("Model", Markup.Escape(settings.Model ?? "(not set)"), Markup.Escape(Origin("Model")));
                table.AddRow("Endpoint", Markup.Escape(settings.Endpoint?.ToString() ?? "(not set)"), Markup.Escape(Origin("Endpoint")));
                table.AddRow("API key", string.IsNullOrEmpty(settings.ApiKey) ? "[grey]not set[/]" : "[green]set[/]", Markup.Escape(Origin("ApiKey")));
                table.AddRow("Timeout", $"{settings.Timeout.TotalSeconds:0}s", Markup.Escape(Origin("Timeout")));
                if (settings.Effort is not null)
                {
                    table.AddRow("Effort", Markup.Escape(settings.Effort), Markup.Escape(Origin("Effort")));
                }
            }

            table.AddRow("Config file", Markup.Escape(settings.ConfigFilePath ?? "-"), settings.ConfigFileFound ? "[green]found[/]" : "[grey]not found[/]");
            var workspace = GitWorkspace.Discover(Environment.CurrentDirectory);
            table.AddRow("Git workspace", Markup.Escape(workspace?.Root ?? "(none)"), Markup.Escape(workspace?.Branch ?? "-"));
            console.Write(table);

            if (settings.Kind == ProviderKind.None)
            {
                console.MarkupLine("[grey]No AI provider selected: analyze prints local hints. Enable one with --provider or DOTNET_DIAG_PROVIDER.[/]");
            }
            else if (settings.Problem is not null)
            {
                console.MarkupLine($"[yellow]warning:[/] {Markup.Escape(settings.Problem)}");
            }
            else
            {
                console.MarkupLine("[green]✔[/] Provider configuration looks usable.");
            }

            return ExitCodes.Success;
        });

        return command;
    }
}
