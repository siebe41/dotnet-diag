using System.Diagnostics;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Workspace;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Logging.StructuredLogger;

namespace DotnetDiag.Tests.Platform;

/// <summary>Path, casing, symlink, file-locking and git behaviors that differ between Windows, macOS and Linux.</summary>
public sealed class FileSystemTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dotnet-diag-fs-" + Guid.NewGuid().ToString("N"));

    public FileSystemTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal); // git marks objects read-only on Windows
            }

            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Parsed_binlog_is_not_left_locked()
    {
        var path = Path.Combine(_directory, "locked.binlog");
        var source = new EventArgsDispatcher();
        var logger = new BinaryLogger { Parameters = path };
        logger.Initialize(source);
        source.Dispatch(new BuildStartedEventArgs("start", "") { BuildEventContext = BuildEventContext.Invalid });
        source.Dispatch(new BuildErrorEventArgs(null, "CS0246", "/r/A.cs", 1, 1, 0, 0, "The type or namespace name 'X' could not be found", null, "Csc") { BuildEventContext = new BuildEventContext(1, 1, 1, 1) });
        source.Dispatch(new BuildFinishedEventArgs("done", "", false) { BuildEventContext = BuildEventContext.Invalid });
        logger.Shutdown();

        Assert.Single(LogParser.ParseFile(path).Errors);

        File.Delete(path); // throws IOException on Windows while a handle is open
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Text_log_is_not_left_locked()
    {
        var path = Path.Combine(_directory, "build.txt");
        File.Copy(Fixtures.Path("syntax-errors.txt"), path);

        Assert.Equal(3, LogParser.ParseFile(path).ErrorCount);
        File.Delete(path);
    }

    [Fact]
    public void Windows_ci_paths_map_onto_the_local_checkout_on_every_os()
    {
        using var repo = new TempRepo();

        var mapped = repo.Workspace.ToRelative(@"D:\a\shop\shop\src\Shop.Api\Services\OrderSerializer.cs");
        var mixed = repo.Workspace.ToRelative(@"src\Shop.Api/Services\OrderSerializer.cs");

        Assert.Equal("src/Shop.Api/Services/OrderSerializer.cs", mapped);
        Assert.Equal("src/Shop.Api/Services/OrderSerializer.cs", mixed);
        Assert.Equal("src/Shop.Api/bin/Debug/net9.0/Shop.Api.dll", repo.Workspace.RelativizeText(@"D:\a\shop\shop\src/Shop.Api/bin/Debug/net9.0/Shop.Api.dll"));
    }

    [Fact]
    public void Terminal_logger_relative_windows_paths_find_their_project()
    {
        using var repo = new TempRepo();
        var log = LogParser.ParseText(@"    src\Shop.Api\Services\OrderSerializer.cs(3,7): error CS0246: The type or namespace name 'Newtonsoft' could not be found (are you missing a using directive or an assembly reference?)");

        var missing = Assert.Single(new LocalDiagnosticEngine(repo.Workspace).Analyze(log).MissingPackages);

        Assert.Equal("src/Shop.Api/Shop.Api.csproj", missing.Project);
    }

    [Fact]
    public void Path_casing_follows_the_file_system()
    {
        using var repo = new TempRepo();
        var actual = repo.Path("src/Shop.Api/Program.cs");
        var wrongCase = repo.Path("SRC/shop.api/program.cs");
        var caseInsensitive = File.Exists(wrongCase); // Windows and default macOS volumes

        var resolved = repo.Workspace.Resolve(wrongCase);

        if (caseInsensitive)
        {
            Assert.NotNull(resolved);
            Assert.True(repo.Workspace.Contains(wrongCase));
            Assert.Equal("src/shop.api/program.cs", repo.Workspace.ToRelative(wrongCase).ToLowerInvariant());
        }
        else
        {
            Assert.Null(resolved);
            Assert.True(repo.Workspace.Contains(actual));
        }
    }

    [Fact]
    public void Checkout_reached_through_a_symlink_is_still_the_workspace()
    {
        // macOS: /var/folders/... is /private/var/folders/...; logs and --repo can disagree.
        using var repo = new TempRepo();
        var linkParent = Path.Combine(_directory, "via-link");
        Directory.CreateDirectory(linkParent);
        var link = Path.Combine(linkParent, Path.GetFileName(repo.Root));
        try
        {
            Directory.CreateSymbolicLink(link, repo.Root);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Windows without Developer Mode/admin cannot create symlinks; nothing to verify there.
            return;
        }

        var workspace = GitWorkspace.Discover(link)!;
        var fileByRealPath = repo.Path("src/Shop.Api/Program.cs");

        Assert.Equal("src/Shop.Api/Program.cs", workspace.ToRelative(fileByRealPath));
        Assert.True(workspace.Contains(fileByRealPath));
    }

    [Fact]
    public void Worktree_style_git_file_reports_its_branch()
    {
        var gitDir = Path.Combine(_directory, "main-repo", ".git", "worktrees", "feature");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/login\n");
        var worktree = Path.Combine(_directory, "feature-worktree");
        Directory.CreateDirectory(Path.Combine(worktree, "src"));
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {gitDir}\n");

        var workspace = GitWorkspace.Discover(Path.Combine(worktree, "src"))!;

        Assert.Equal(Path.GetFullPath(worktree), workspace.Root);
        Assert.Equal("feature/login", workspace.Branch);
    }

    [Fact]
    public void Real_git_repository_reports_branch_and_uncommitted_files()
    {
        if (!Git("--version", _directory, out _))
        {
            return; // git is installed on every CI runner; local machines without it cannot run this check
        }

        var root = Path.Combine(_directory, "real repo with spaces");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "Clean.cs"), "class Clean {}\n");
        File.WriteAllText(Path.Combine(root, "src", "Dirty.cs"), "class Dirty {}\n");
        Assert.True(Git("init -q -b diag-test", root, out var init), init);
        Assert.True(Git("add -A", root, out var add), add);
        Assert.True(Git("-c user.email=t@t -c user.name=t -c commit.gpgsign=false commit -q -m init", root, out var commit), commit);
        File.AppendAllText(Path.Combine(root, "src", "Dirty.cs"), "// changed\n");
        File.WriteAllText(Path.Combine(root, "src", "New File.cs"), "class NewFile {}\n");

        var workspace = GitWorkspace.Discover(Path.Combine(root, "src"))!;

        Assert.Equal("diag-test", workspace.Branch);
        Assert.True(workspace.IsModified(Path.Combine(root, "src", "Dirty.cs")));
        Assert.True(workspace.IsModified(Path.Combine(root, "src", "New File.cs")));
        Assert.False(workspace.IsModified(Path.Combine(root, "src", "Clean.cs")));
    }

    private static bool Git(string arguments, string workingDirectory, out string output)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }
}
