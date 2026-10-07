using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Tests;

internal static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Read(string name) => File.ReadAllText(Path(name));

    public static ParsedLog Parse(string name) => LogParser.ParseFile(Path(name));

    public static readonly string[] TextLogs =
    [
        "missing-package-cs0246.txt",
        "syntax-errors.txt",
        "nuget-restore.txt",
        "version-conflict.txt",
        "dotnet-test-vstest.txt",
        "dotnet-test-mtp.txt",
        "github-actions-build.txt",
    ];
}

/// <summary>A copy of Fixtures/SampleRepo in a temp folder with a minimal .git directory.</summary>
internal sealed class TempRepo : IDisposable
{
    public TempRepo(string branch = "main")
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotnet-diag-tests", Guid.NewGuid().ToString("N"));
        CopyDirectory(Fixtures.Path("SampleRepo"), Root);
        Directory.CreateDirectory(System.IO.Path.Combine(Root, ".git"));
        File.WriteAllText(System.IO.Path.Combine(Root, ".git", "HEAD"), $"ref: refs/heads/{branch}\n");
        Workspace = GitWorkspace.Discover(Root)!;
    }

    public string Root { get; }

    public GitWorkspace Workspace { get; }

    public string Path(string relative) => System.IO.Path.Combine(Root, relative);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
