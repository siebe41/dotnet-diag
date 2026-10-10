using System.Diagnostics;

namespace DotnetDiag.Core.Workspace;

/// <summary>
/// The git working tree the log belongs to. Used to resolve paths from logs (including logs produced
/// on a CI machine with a different checkout root), read source context and flag locally modified files.
/// </summary>
public sealed class GitWorkspace
{
    private readonly HashSet<string> _foreignRoots = new(StringComparer.Ordinal);
    private IReadOnlySet<string>? _modifiedFiles;

    private GitWorkspace(string root, bool isGitRepository)
    {
        Root = Path.GetFullPath(root);
        IsGitRepository = isGitRepository;
    }

    public string Root { get; }

    public bool IsGitRepository { get; }

    /// <summary>Current branch name, or a short commit id for a detached HEAD.</summary>
    public string? Branch => IsGitRepository ? ReadBranch() : null;

    /// <summary>Walks up from <paramref name="startDirectory"/> looking for a <c>.git</c> directory or file.</summary>
    public static GitWorkspace? Discover(string startDirectory)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (dir is not null)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return new GitWorkspace(dir.FullName, isGitRepository: true);
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Uses <paramref name="directory"/> as the root even when it is not a git repository.</summary>
    public static GitWorkspace ForDirectory(string directory) =>
        Discover(directory) is { } git && PathEquals(git.Root, directory)
            ? git
            : new GitWorkspace(directory, Directory.Exists(Path.Combine(directory, ".git")));

    /// <summary>Repository-relative files with uncommitted changes (empty when git is unavailable).</summary>
    public IReadOnlySet<string> ModifiedFiles => _modifiedFiles ??= LoadModifiedFiles();

    public bool IsModified(string path)
    {
        var relative = ToRelative(path);
        return ModifiedFiles.Contains(relative);
    }

    /// <summary>
    /// Maps a path from a log to an existing file in this workspace, or null. Only files that
    /// <see cref="PathConfinement.IsReadable"/> allows are returned: inside <see cref="Root"/> after resolving
    /// <c>..</c> and symbolic links, and not below a folder or named like a file that starts with a dot.
    /// </summary>
    public string? Resolve(string pathFromLog)
    {
        if (string.IsNullOrWhiteSpace(pathFromLog))
        {
            return null;
        }

        var normalized = pathFromLog.Replace('\\', '/');
        try
        {
            if (Path.IsPathRooted(normalized) && Confined(normalized) is { } rooted)
            {
                return rooted;
            }

            if (!IsRootedAnywhere(normalized) && Confined(Path.Combine(Root, normalized)) is { } relative)
            {
                return relative;
            }

            // Path from another machine (e.g. /home/runner/work/app/app/src/App/Program.cs or D:\a\app\src\...),
            // or from another checkout on this one: drop leading segments until the remainder exists under this root.
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var skip = 1; skip < segments.Length; skip++)
            {
                if (Confined(Path.Combine([Root, .. segments[skip..]])) is { } mapped)
                {
                    // Remember the CI checkout root so other paths from the same log can be made relative
                    // even when they do not exist locally (e.g. build outputs, other projects).
                    lock (_foreignRoots)
                    {
                        _foreignRoots.Add(string.Join('/', segments[..skip]));
                    }

                    return mapped;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private string? Confined(string candidate)
    {
        var full = Path.GetFullPath(candidate);
        return File.Exists(full) && PathConfinement.IsReadable(Root, full) ? full : null;
    }

    /// <summary>True when <paramref name="path"/> resolves to a file inside this workspace.</summary>
    public bool Contains(string path) =>
        Resolve(path) is { } resolved && !Path.IsPathRooted(ToRelative(resolved));

    /// <summary>Repository-relative, forward-slash path when inside the workspace; otherwise the input.</summary>
    public string ToRelative(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var resolved = Resolve(path) ?? path;
        try
        {
            var full = Path.GetFullPath(resolved);
            var relative = Path.GetRelativePath(Root, full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            {
                return relative.Replace('\\', '/');
            }

            if (MapThroughRootName(full) is { } mapped)
            {
                return mapped;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
        }

        var normalized = path.Replace('\\', '/');
        lock (_foreignRoots)
        {
            foreach (var foreignRoot in _foreignRoots)
            {
                var trimmed = normalized.TrimStart('/');
                if (trimmed.StartsWith(foreignRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[(foreignRoot.Length + 1)..];
                }
            }
        }

        return normalized;
    }

    /// <summary>Replaces the local root and any learned CI checkout roots inside free text (e.g. messages).</summary>
    public string RelativizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text
            .Replace(Root.Replace('\\', '/') + "/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(Root + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase);
        lock (_foreignRoots)
        {
            foreach (var foreignRoot in _foreignRoots)
            {
                result = result.Replace("/" + foreignRoot + "/", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace(foreignRoot.Replace('/', '\\') + "\\", string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    /// <summary>
    /// Handles the same checkout reached through another path, e.g. macOS's /var → /private/var symlink or a
    /// symlinked/junctioned source folder: find the repository folder name in the path and check that the
    /// remainder exists under this root.
    /// </summary>
    private string? MapThroughRootName(string fullPath)
    {
        var rootName = Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(rootName))
        {
            return null;
        }

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var segments = fullPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!segments[i].Equals(rootName, comparison))
            {
                continue;
            }

            var rest = segments[(i + 1)..];
            if (File.Exists(Path.Combine([Root, .. rest])))
            {
                return string.Join('/', rest);
            }
        }

        return null;
    }

    private static bool IsRootedAnywhere(string path) =>
        path.StartsWith('/') || (path.Length > 2 && path[1] == ':' && char.IsLetter(path[0]));

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private string? ReadBranch()
    {
        try
        {
            var gitPath = Path.Combine(Root, ".git");
            if (File.Exists(gitPath))
            {
                // Worktree or submodule: ".git" is a file containing "gitdir: <path>".
                var pointer = File.ReadAllText(gitPath).Trim();
                if (!pointer.StartsWith("gitdir:", StringComparison.Ordinal))
                {
                    return null;
                }

                gitPath = Path.GetFullPath(Path.Combine(Root, pointer["gitdir:".Length..].Trim()));
            }

            var head = Path.Combine(gitPath, "HEAD");
            if (!File.Exists(head))
            {
                return null;
            }

            var content = File.ReadAllText(head).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (content.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                return content[refPrefix.Length..];
            }

            return content.Length >= 7 ? content[..7] : content;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IReadOnlySet<string> LoadModifiedFiles()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!IsGitRepository)
        {
            return result;
        }

        try
        {
            var psi = new ProcessStartInfo("git", ["-C", Root, "status", "--porcelain=v1", "--untracked-files=normal"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return result;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000) || process.ExitCode != 0)
            {
                return result;
            }

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 4)
                {
                    continue;
                }

                var path = line[3..].Trim();
                var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    path = path[(arrow + 4)..];
                }

                result.Add(path.Trim('"'));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // git is not installed or not on PATH: modified-file hints are simply unavailable.
        }

        return result;
    }
}
