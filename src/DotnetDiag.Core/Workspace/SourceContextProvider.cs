namespace DotnetDiag.Core.Workspace;

/// <summary>
/// Reads source lines around a location, resolving log paths through the git workspace. Without a workspace,
/// only files under the current directory are read, so a log analyzed next to the project it came from still
/// gets its snippets; a path from a log never reaches a file outside the folder being analyzed.
/// </summary>
public sealed class SourceContextProvider(GitWorkspace? workspace)
{
    private const int MaxLineLength = 160;
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public SourceSnippet? GetSnippet(string pathFromLog, int line, int radius = 3)
    {
        if (line <= 0)
        {
            return null;
        }

        var path = PathConfinement.ResolveFromLog(pathFromLog, workspace);
        if (path is null)
        {
            return null;
        }

        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return null;
            }

            var all = File.ReadAllLines(path);
            if (line > all.Length)
            {
                return null;
            }

            var start = Math.Max(1, line - radius);
            var end = Math.Min(all.Length, line + radius);
            var lines = new List<SourceLine>();
            for (var i = start; i <= end; i++)
            {
                var text = all[i - 1].TrimEnd();
                if (text.Length > MaxLineLength)
                {
                    text = string.Concat(text.AsSpan(0, MaxLineLength - 1), "…");
                }

                lines.Add(new SourceLine(i, text, i == line));
            }

            var relative = workspace?.ToRelative(path) ?? Path.GetFileName(path);
            var modified = workspace?.IsModified(path) ?? false;
            return new SourceSnippet(relative, line, lines, modified);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
