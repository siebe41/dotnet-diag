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

    /// <summary>Maps a path from a log to an existing file in this workspace, or null.</summary>
    public string? Resolve(string pathFromLog)
    {
        if (string.IsNullOrWhiteSpace(pathFromLog))
        {
            return null;
        }

        var normalized = pathFromLog.Replace('\\', '/');
        try
        {
            if (Path.IsPathRooted(normalized) && File.Exists(normalized))
            {
                return Path.GetFullPath(normalized);
            }

            if (!IsRootedAnywhere(normalized))
            {
                var candidate = Path.GetFullPath(Path.Combine(Root, normalized));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Path from another machine (e.g. /home/runner/work/app/app/src/App/Program.cs or D:\a\app\src\...):
            // drop leading segments until the remainder exists under this root.
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var skip = 1; skip < segments.Length; skip++)
            {
                var candidate = Path.Combine([Root, .. segments[skip..]]);
                if (File.Exists(candidate))
                {
                    // Remember the CI checkout root so other paths from the same log can be made relative
                    // even when they do not exist locally (e.g. build outputs, other projects).
                    lock (_foreignRoots)
                    {
                        _foreignRoots.Add(string.Join('/', segments[..skip]));
                    }

                    return Path.GetFullPath(candidate);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

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
