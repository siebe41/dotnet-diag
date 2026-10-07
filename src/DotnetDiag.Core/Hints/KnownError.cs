namespace DotnetDiag.Core.Hints;

/// <summary>Static knowledge about a diagnostic code.</summary>
public sealed record KnownError(string Code, string Title, string Explanation, IReadOnlyList<string> Fixes)
{
    public string DocsUrl => KnownErrorCatalog.DocsUrlFor(Code);
}
