namespace DotnetDiag.Core.Models;

/// <summary>
/// A single MSBuild/compiler/NuGet diagnostic, e.g. <c>Program.cs(5,7): error CS0246: ...</c>.
/// </summary>
public sealed record BuildDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? File = null,
    int? Line = null,
    int? Column = null,
    string? Project = null,
    string? TargetFramework = null)
{
    public DiagnosticCategory Category => DiagnosticCategories.FromCode(Code);

    /// <summary>"path:line:col", "path:line", "path", or empty when no file is known.</summary>
    public string FormatLocation(Func<string, string>? pathFormatter = null)
    {
        if (string.IsNullOrEmpty(File))
        {
            return string.Empty;
        }

        var path = pathFormatter is null ? File : pathFormatter(File);
        return (Line, Column) switch
        {
            ({ } l, { } c) when l > 0 && c > 0 => $"{path}:{l}:{c}",
            ({ } l, _) when l > 0 => $"{path}:{l}",
            _ => path,
        };
    }

    /// <summary>True when the diagnostic points into MSBuild/SDK targets rather than the user's code.</summary>
    public bool IsInBuildTooling =>
        File is not null && (File.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
                             File.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                             File.EndsWith(".tasks", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Location to show people: the project for diagnostics raised inside SDK .targets files (the targets path
    /// is noise), otherwise <see cref="FormatLocation"/>, falling back to the project.
    /// </summary>
    public string DisplayLocation(Func<string, string>? pathFormatter = null)
    {
        if (IsInBuildTooling && Project is not null)
        {
            return pathFormatter is null ? Project : pathFormatter(Project);
        }

        var location = FormatLocation(pathFormatter);
        return location.Length == 0 && Project is not null
            ? (pathFormatter is null ? Project : pathFormatter(Project))
            : location;
    }

    /// <summary>Key used to drop the duplicate copies that MSBuild prints in its end-of-build summary.</summary>
    internal (string, DiagnosticSeverity, string, string?, int?, int?) DedupKey =>
        (Code.ToUpperInvariant(), Severity, Message, File?.Replace('\\', '/'), Line, Column);
}
