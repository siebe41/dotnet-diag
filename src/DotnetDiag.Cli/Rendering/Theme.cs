using DotnetDiag.Core.Models;
using Spectre.Console;

namespace DotnetDiag.Cli.Rendering;

/// <summary>Colors used consistently across the terminal output.</summary>
internal static class Theme
{
    public static readonly Style Accent = new(Color.DeepSkyBlue1);
    public static readonly Style Border = new(Color.Grey35);

    public static string CategoryColor(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.CSharpCompiler or DiagnosticCategory.FSharpCompiler or DiagnosticCategory.VisualBasicCompiler => "deepskyblue1",
        DiagnosticCategory.MSBuild => "darkorange",
        DiagnosticCategory.NuGet => "mediumpurple1",
        DiagnosticCategory.DotnetSdk => "aqua",
        DiagnosticCategory.Analyzer => "grey70",
        _ => "silver",
    };

    public static string SeverityColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "red",
        DiagnosticSeverity.Warning => "yellow",
        _ => "grey",
    };

    public static string SeverityIcon(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "[red]✖[/]",
        DiagnosticSeverity.Warning => "[yellow]▲[/]",
        _ => "[grey]•[/]",
    };

    public static string CodeMarkup(string code, DiagnosticCategory category) =>
        $"[bold {CategoryColor(category)}]{Markup.Escape(code)}[/]";
}
