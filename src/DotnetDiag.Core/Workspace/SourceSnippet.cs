namespace DotnetDiag.Core.Workspace;

public sealed record SourceLine(int Number, string Text, bool IsTarget);

/// <summary>A few lines of source around a diagnostic or failing stack frame.</summary>
public sealed record SourceSnippet(string RelativePath, int TargetLine, IReadOnlyList<SourceLine> Lines, bool IsModified)
{
    public string Language => Path.GetExtension(RelativePath).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".fs" => "fsharp",
        ".vb" => "vb",
        ".csproj" or ".fsproj" or ".vbproj" or ".props" or ".targets" or ".xml" => "xml",
        ".json" => "json",
        ".razor" or ".cshtml" => "razor",
        _ => string.Empty,
    };

    public string ToCodeBlock()
    {
        var width = Lines.Count == 0 ? 1 : Lines.Max(l => l.Number).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        var body = string.Join('\n', Lines.Select(l =>
            $"{(l.IsTarget ? ">" : " ")}{l.Number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width)} | {l.Text}"));
        return $"```{Language}\n{body}\n```";
    }
}
