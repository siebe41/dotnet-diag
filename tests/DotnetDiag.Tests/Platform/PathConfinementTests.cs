using DotnetDiag.Core.Context;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Tests.Platform;

/// <summary>
/// Paths in a log are untrusted: a crafted log must not get the contents of a file outside the workspace (or a
/// dot-folder such as .git or .ssh inside it) into the bundle that is sent to a model.
/// </summary>
public sealed class PathConfinementTests : IDisposable
{
    private const string Secret = "TOP-SECRET-LINE";

    private readonly string _outside = Path.Combine(Path.GetTempPath(), "dotnet-diag-outside-" + Guid.NewGuid().ToString("N"));
    private readonly TempRepo _repo = new();

    public PathConfinementTests()
    {
        Directory.CreateDirectory(_outside);
        File.WriteAllText(SecretFile, $"line 1\n{Secret}\nline 3\n");
    }

    private string SecretFile => Path.Combine(_outside, "secret.cs");

    public void Dispose()
    {
        _repo.Dispose();
        try
        {
            Directory.Delete(_outside, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void In_repo_and_ci_mapped_paths_still_resolve()
    {
        var program = Path.GetFullPath(_repo.Path("src/Shop.Api/Program.cs"));

        Assert.Equal(program, _repo.Workspace.Resolve("src/Shop.Api/Program.cs"));
        Assert.Equal(program, _repo.Workspace.Resolve(program));
        Assert.Equal(program, _repo.Workspace.Resolve("/home/runner/work/shop/shop/src/Shop.Api/Program.cs"));
        Assert.Equal(program, _repo.Workspace.Resolve(@"D:\a\shop\shop\src\Shop.Api\Program.cs"));
        Assert.NotNull(new SourceContextProvider(_repo.Workspace).GetSnippet("/home/runner/work/shop/shop/src/Shop.Api/Program.cs", 3));
    }

    [Fact]
    public void Absolute_path_outside_the_root_is_refused()
    {
        Assert.True(File.Exists(SecretFile));
        Assert.Null(_repo.Workspace.Resolve(SecretFile));
        Assert.False(_repo.Workspace.Contains(SecretFile));
        Assert.Null(new SourceContextProvider(_repo.Workspace).GetSnippet(SecretFile, 2));
        AssertNotInBundle(SecretFile);
    }

    [Fact]
    public void Dot_dot_escape_is_refused()
    {
        var relative = Path.GetRelativePath(_repo.Root, SecretFile).Replace('\\', '/');
        Assert.StartsWith("../", relative);

        Assert.Null(_repo.Workspace.Resolve(relative));
        Assert.Null(_repo.Workspace.Resolve("src/Shop.Api/../../" + relative));
        Assert.Null(new SourceContextProvider(_repo.Workspace).GetSnippet(relative, 2));
        AssertNotInBundle(relative);

        // A CI-style prefix in front of the escape: stripping leading segments must not walk out of the root either.
        Assert.Null(_repo.Workspace.Resolve("/home/runner/work/shop/shop/" + relative));
    }

    [Fact]
    public void Sibling_folder_sharing_the_root_prefix_is_outside()
    {
        var sibling = _repo.Root + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            var file = Path.Combine(sibling, "a.cs");
            File.WriteAllText(file, "class A {}\n");

            Assert.Null(_repo.Workspace.Resolve(file));
            Assert.False(PathConfinement.IsReadable(_repo.Root, file));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void Symlink_inside_the_repo_pointing_outside_is_refused()
    {
        var fileLink = _repo.Path("src/Shop.Api/Leak.cs");
        var folderLink = _repo.Path("src/Linked");
        try
        {
            File.CreateSymbolicLink(fileLink, SecretFile);
            Directory.CreateSymbolicLink(folderLink, _outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Windows without Developer Mode/admin cannot create symlinks; nothing to verify there.
            return;
        }

        Assert.True(File.Exists(fileLink));
        Assert.Null(_repo.Workspace.Resolve("src/Shop.Api/Leak.cs"));
        Assert.Null(_repo.Workspace.Resolve(fileLink));
        Assert.Null(_repo.Workspace.Resolve("src/Linked/secret.cs"));
        Assert.Null(_repo.Workspace.Resolve("/home/runner/work/shop/shop/src/Linked/secret.cs"));
        AssertNotInBundle("src/Shop.Api/Leak.cs");
        AssertNotInBundle("src/Linked/secret.cs");

        // A link that stays inside the repository is still followed.
        var innerLink = _repo.Path("src/Shop.Api/ProgramLink.cs");
        File.CreateSymbolicLink(innerLink, _repo.Path("src/Shop.Api/Program.cs"));
        Assert.NotNull(_repo.Workspace.Resolve("src/Shop.Api/ProgramLink.cs"));
    }

    [Fact]
    public void Dot_segments_are_refused()
    {
        File.WriteAllText(_repo.Path(".env"), $"API_KEY={Secret}\n");
        Directory.CreateDirectory(_repo.Path("src/.ssh"));
        File.WriteAllText(_repo.Path("src/.ssh/id_rsa.cs"), $"x\n{Secret}\n");

        foreach (var path in new[] { ".env", ".git/HEAD", "src/.ssh/id_rsa.cs", "/home/runner/work/shop/shop/.git/HEAD" })
        {
            Assert.Null(_repo.Workspace.Resolve(path));
            Assert.Null(new SourceContextProvider(_repo.Workspace).GetSnippet(path, 1));
        }

        Assert.Null(_repo.Workspace.Resolve(_repo.Path(".env")));
        AssertNotInBundle("src/.ssh/id_rsa.cs");
    }

    [Fact]
    public void Without_a_workspace_only_files_under_the_given_folder_are_read()
    {
        var program = _repo.Path("src/Shop.Api/Program.cs");

        Assert.Equal(Path.GetFullPath(program), PathConfinement.ResolveUnder(_repo.Root, "src/Shop.Api/Program.cs"));
        Assert.Equal(Path.GetFullPath(program), PathConfinement.ResolveUnder(_repo.Root, program));
        Assert.Null(PathConfinement.ResolveUnder(_repo.Root, SecretFile));
        Assert.Null(PathConfinement.ResolveUnder(_repo.Root, Path.GetRelativePath(_repo.Root, SecretFile)));
        Assert.Null(PathConfinement.ResolveUnder(_repo.Root, ".git/HEAD"));

        // No workspace: confined to the current directory, which the temp folder is not under.
        Assert.Null(new SourceContextProvider(null).GetSnippet(SecretFile, 2));
    }

    [Fact]
    public void Package_references_are_read_only_inside_the_root()
    {
        // A project outside the root is not read, even when a log names it.
        var outsideProject = Path.Combine(_outside, "Outside.csproj");
        File.WriteAllText(outsideProject, """<Project><ItemGroup><PackageReference Include="Leaked.Package" Version="1.0.0" /></ItemGroup></Project>""");
        Assert.Empty(ProjectInspector.GetPackageReferences(outsideProject, _repo.Root));
        Assert.Null(_repo.Workspace.Resolve(outsideProject));

        // Directory.Packages.props above a non-git root is not read either.
        var parent = Path.Combine(_outside, "parent");
        var root = Path.Combine(parent, "app");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(parent, "Directory.Packages.props"), """<Project><ItemGroup><PackageVersion Include="Some.Package" Version="6.6.6" /></ItemGroup></Project>""");
        var project = Path.Combine(root, "App.csproj");
        File.WriteAllText(project, """<Project><ItemGroup><PackageReference Include="Some.Package" /></ItemGroup></Project>""");

        var reference = Assert.Single(ProjectInspector.GetPackageReferences(project, root));
        Assert.Equal("Some.Package", reference.Name);
        Assert.Null(reference.Version);

        // Inside the root, central versions still apply.
        var inRepo = Assert.Single(ProjectInspector.GetPackageReferences(_repo.Path("src/Shop.Api/Shop.Api.csproj"), _repo.Root), r => r.Name == "Microsoft.Extensions.Http");
        Assert.Equal("9.0.0", inRepo.Version);
    }

    private void AssertNotInBundle(string pathInLog)
    {
        var log = LogParser.ParseText($"{pathInLog}(2,1): error CS1002: ; expected [{_repo.Path("src/Shop.Api/Shop.Api.csproj")}]");
        Assert.Single(log.Errors);

        var bundle = new ContextBuilder().Build(log, new LocalDiagnosticEngine(_repo.Workspace).Analyze(log), _repo.Workspace);

        Assert.DoesNotContain(Secret, bundle.Text);
        Assert.Empty(bundle.Snippets);
    }
}
