namespace DotnetDiag.Core.Workspace;

/// <summary>
/// Decides which files a path taken from a log may read. Logs can come from another machine or another person,
/// so a path in one is untrusted: only regular files inside the workspace root are read, after resolving
/// <c>..</c> and symbolic links, and never files or folders whose name starts with a dot (<c>.git</c>,
/// <c>.env</c>, <c>.ssh</c>, ...).
/// </summary>
public static class PathConfinement
{
    private const int MaxLinkDepth = 32;

    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>Windows and macOS file systems are case-insensitive by default; Linux is not.</summary>
    private static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The folder a log's paths are confined to: the workspace root, or the current directory without one.</summary>
    public static string RootFor(GitWorkspace? workspace) => workspace?.Root ?? Environment.CurrentDirectory;

    /// <summary>
    /// The file a path from a log refers to when it may be read: mapped through <paramref name="workspace"/>
    /// (which also handles paths from a CI checkout), or confined to the current directory without one.
    /// </summary>
    public static string? ResolveFromLog(string pathFromLog, GitWorkspace? workspace) =>
        workspace is not null ? workspace.Resolve(pathFromLog) : ResolveUnder(RootFor(null), pathFromLog);

    /// <summary>
    /// The full path of <paramref name="pathFromLog"/> (relative paths are taken against <paramref name="root"/>)
    /// when it is an existing file that may be read; otherwise null.
    /// </summary>
    public static string? ResolveUnder(string root, string pathFromLog)
    {
        if (string.IsNullOrWhiteSpace(pathFromLog))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(pathFromLog.Replace('\\', '/'), Path.GetFullPath(root));
            return File.Exists(full) && IsReadable(root, full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> is inside <paramref name="root"/> once both are made absolute and every
    /// symbolic link or junction along them is resolved, and no segment below the root starts with a dot.
    /// A path that reaches the root through a link (macOS <c>/var</c> → <c>/private/var</c>) is inside;
    /// a link inside the root that points outside it is not.
    /// </summary>
    public static bool IsReadable(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(path);

            // The path as written: refuse dot segments even if a link would take them somewhere harmless.
            if (RelativeSegments(fullRoot, fullPath) is { } lexical && lexical.Any(IsHidden))
            {
                return false;
            }

            return RelativeSegments(Canonicalize(fullRoot), Canonicalize(fullPath)) is { Length: > 0 } real
                && !real.Any(IsHidden);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The segments of <paramref name="path"/> below <paramref name="root"/>, or null when it is not below it.</summary>
    private static string[]? RelativeSegments(string root, string path)
    {
        var trimmedRoot = root.TrimEnd(Separators);
        if (path.Length <= trimmedRoot.Length + 1
            || !path.StartsWith(trimmedRoot, Comparison)
            || !Separators.Contains(path[trimmedRoot.Length]))
        {
            return null;
        }

        return path[(trimmedRoot.Length + 1)..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsHidden(string segment) => segment.StartsWith('.');

    /// <summary>
    /// <paramref name="fullPath"/> with every symbolic link or junction along it replaced by its final target.
    /// Segments that do not exist are kept as written.
    /// </summary>
    private static string Canonicalize(string fullPath, int depth = 0)
    {
        if (depth > MaxLinkDepth)
        {
            throw new IOException("Too many levels of symbolic links.");
        }

        var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = pathRoot;
        foreach (var segment in fullPath[pathRoot.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                // The target can itself sit below another link (e.g. a relative target through a linked folder).
                next = Canonicalize(Path.GetFullPath(target.FullName), depth + 1);
            }

            current = next;
        }

        return current;
    }
}
