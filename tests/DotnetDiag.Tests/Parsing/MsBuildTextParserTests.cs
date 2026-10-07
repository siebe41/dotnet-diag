using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Tests.Parsing;

public class MsBuildTextParserTests
{
    [Fact]
    public void Parses_compiler_errors_and_drops_end_of_build_summary_duplicates()
    {
        var log = Fixtures.Parse("missing-package-cs0246.txt");

        Assert.Equal(InputKind.BuildOutput, log.Kind);
        Assert.False(log.BuildSucceeded);
        Assert.Equal(5, log.ErrorCount);
        Assert.Equal(1, log.WarningCount);
        Assert.Equal(["CS0246", "CS0103", "CS0103", "CS1061", "CS0006"], log.Errors.Select(e => e.Code));
    }

    [Fact]
    public void Extracts_file_position_project_and_message()
    {
        var log = Fixtures.Parse("missing-package-cs0246.txt");
        var cs0246 = log.Errors.First();

        Assert.Equal("/home/runner/work/shop/shop/src/Shop.Api/Services/OrderSerializer.cs", cs0246.File);
        Assert.Equal(3, cs0246.Line);
        Assert.Equal(7, cs0246.Column);
        Assert.Equal("/home/runner/work/shop/shop/src/Shop.Api/Shop.Api.csproj", cs0246.Project);
        Assert.StartsWith("The type or namespace name 'Newtonsoft' could not be found", cs0246.Message);
        Assert.DoesNotContain("[/home", cs0246.Message);
        Assert.Equal(DiagnosticCategory.CSharpCompiler, cs0246.Category);
    }

    [Fact]
    public void Tool_origins_are_not_treated_as_files()
    {
        var log = Fixtures.Parse("missing-package-cs0246.txt");
        var cs0006 = log.Errors.Single(e => e.Code == "CS0006");

        Assert.Null(cs0006.File);
        Assert.EndsWith("Shop.Api.Tests.csproj", cs0006.Project);
    }

    [Fact]
    public void Parses_terminal_logger_output()
    {
        var log = Fixtures.Parse("syntax-errors.txt");

        Assert.False(log.BuildSucceeded);
        Assert.Equal(["CS1002", "CS1513", "CS1026"], log.Errors.Select(e => e.Code));
        Assert.All(log.Errors, e => Assert.Null(e.Project));
        Assert.Equal(12, log.Errors.First().Line);
        Assert.Equal(35, log.Errors.First().Column);
    }

    [Fact]
    public void Parses_windows_paths_with_spaces_and_target_framework_suffix()
    {
        var log = Fixtures.Parse("version-conflict.txt");

        var msb3277 = log.Warnings.Single();
        Assert.Equal("MSB3277", msb3277.Code);
        Assert.Equal(@"C:\Program Files\dotnet\sdk\9.0.100\Microsoft.Common.CurrentVersion.targets", msb3277.File);
        Assert.Equal(2413, msb3277.Line);
        Assert.Equal(5, msb3277.Column);

        var msb4018 = log.Errors.Single(e => e.Code == "MSB4018");
        Assert.Equal(@"D:\a\legacy\legacy\src\Legacy.Web\Legacy.Web.csproj", msb4018.Project);
        Assert.Equal("net9.0", msb4018.TargetFramework);
        Assert.Equal(DiagnosticCategory.MSBuild, msb4018.Category);
    }

    [Fact]
    public void NuGet_errors_are_attributed_to_the_project_not_the_solution()
    {
        var log = Fixtures.Parse("nuget-restore.txt");
        var nu1101 = log.Errors.Single(e => e.Code == "NU1101");

        Assert.Equal("/src/app/src/Billing/Billing.csproj", nu1101.Project);
        Assert.Null(nu1101.Line);
        Assert.Equal(DiagnosticCategory.NuGet, nu1101.Category);
        Assert.Contains("/src/app/src/Billing/Billing.csproj", log.Projects);
        Assert.DoesNotContain(log.Projects, p => p.EndsWith(".sln", StringComparison.Ordinal));
    }

    [Fact]
    public void NuGet_dependency_chain_lines_are_merged_into_one_diagnostic()
    {
        var log = Fixtures.Parse("nuget-restore.txt");

        var nu1605 = Assert.Single(log.Errors, e => e.Code == "NU1605");
        Assert.Equal(3, nu1605.Message.Split('\n').Length);
        Assert.Contains("Billing -> Microsoft.Extensions.Http 9.0.0 -> Microsoft.Extensions.Logging.Abstractions (>= 9.0.0)", nu1605.Message);
        Assert.Equal(3, log.ErrorCount);
    }

    [Fact]
    public void Strips_ci_timestamps_ansi_escapes_and_annotations()
    {
        var log = Fixtures.Parse("github-actions-build.txt");

        var error = Assert.Single(log.Errors);
        Assert.Equal("CS1061", error.Code);
        Assert.Equal("/home/runner/work/shop/shop/src/Shop.Api/Program.cs", error.File);
        Assert.DoesNotContain('\u001b', error.Message);
        Assert.False(log.BuildSucceeded);
    }

    [Theory]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.", "MSB1009", DiagnosticSeverity.Error, null)]
    [InlineData("warning NU1903: Package 'Newtonsoft.Json' 12.0.1 has a known high severity vulnerability", "NU1903", DiagnosticSeverity.Warning, null)]
    [InlineData("/repo/App/Program.cs(1,1): Warning CA1822: Member 'Run' does not access instance data", "CA1822", DiagnosticSeverity.Warning, "/repo/App/Program.cs")]
    [InlineData("C:\\src\\App\\Program.cs(10): error CS0161: 'Program.Get()': not all code paths return a value [C:\\src\\App\\App.csproj]", "CS0161", DiagnosticSeverity.Error, "C:\\src\\App\\Program.cs")]
    [InlineData("/repo/Tests/T.cs(4,6,4,10): warning xUnit1013: Public method 'Helper' on test class should be marked as a Fact", "xUnit1013", DiagnosticSeverity.Warning, "/repo/Tests/T.cs")]
    public void Parses_individual_diagnostic_lines(string line, string code, DiagnosticSeverity severity, string? file)
    {
        Assert.True(MsBuildTextParser.TryParseLine(line, out var diagnostic));
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal(file, diagnostic.File);
    }

    [Theory]
    [InlineData("    0 Error(s)")]
    [InlineData("    1 Warning(s)")]
    [InlineData("Build succeeded.")]
    [InlineData("  Restored /src/App/App.csproj (in 120 ms).")]
    [InlineData("info: Microsoft.Hosting.Lifetime[0] Application started. Logging error rate")]
    public void Ignores_lines_that_are_not_diagnostics(string line)
    {
        Assert.False(MsBuildTextParser.TryParseLine(line, out _));
    }

    [Fact]
    public void Detects_successful_build()
    {
        var log = LogParser.ParseText("  App -> /src/App/bin/Debug/net9.0/App.dll\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n");

        Assert.True(log.BuildSucceeded);
        Assert.False(log.HasProblems);
    }
}
