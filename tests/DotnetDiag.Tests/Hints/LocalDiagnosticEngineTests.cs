using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Tests.Hints;

public class LocalDiagnosticEngineTests
{
    [Fact]
    public void Suggests_missing_packages_for_unresolved_types_and_extension_methods()
    {
        using var repo = new TempRepo();
        var analysis = new LocalDiagnosticEngine(repo.Workspace).Analyze(Fixtures.Parse("missing-package-cs0246.txt"));

        var newtonsoft = analysis.MissingPackages.Single(m => m.Package == "Newtonsoft.Json");
        Assert.Equal(MissingPackageReason.NotReferenced, newtonsoft.Reason);
        Assert.Equal("src/Shop.Api/Shop.Api.csproj", newtonsoft.Project);
        Assert.Equal("dotnet add src/Shop.Api/Shop.Api.csproj package Newtonsoft.Json", newtonsoft.SuggestedCommand);

        var efCore = analysis.MissingPackages.Single(m => m.Package == "Microsoft.EntityFrameworkCore");
        Assert.Equal(MissingPackageReason.NotReferenced, efCore.Reason);
        Assert.Equal(2, analysis.MissingPackages.Count);
    }

    [Fact]
    public void Package_suggestions_align_with_the_projects_package_family_version()
    {
        // The sample project references Microsoft.Extensions.Http 9.0.0 (via Directory.Packages.props); the latest
        // EF Core would require newer Microsoft.Extensions.* packages and fail restore with NU1605.
        using var repo = new TempRepo();
        var analysis = new LocalDiagnosticEngine(repo.Workspace).Analyze(Fixtures.Parse("missing-package-cs0246.txt"));

        var efCore = analysis.MissingPackages.Single(m => m.Package == "Microsoft.EntityFrameworkCore");
        Assert.Equal("9.0.0", efCore.Version);
        Assert.Equal("dotnet add src/Shop.Api/Shop.Api.csproj package Microsoft.EntityFrameworkCore --version 9.0.0", efCore.SuggestedCommand);
        Assert.Null(analysis.MissingPackages.Single(m => m.Package == "Newtonsoft.Json").Version); // unrelated family: latest
    }

    [Fact]
    public void Detects_referenced_package_with_missing_using()
    {
        using var repo = new TempRepo();
        var csproj = repo.Path("src/Shop.Api/Shop.Api.csproj");
        File.WriteAllText(csproj, File.ReadAllText(csproj).Replace(
            "<PackageReference Include=\"Serilog.AspNetCore\" Version=\"8.0.3\" />",
            "<PackageReference Include=\"Serilog.AspNetCore\" Version=\"8.0.3\" /><PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />",
            StringComparison.Ordinal));

        var analysis = new LocalDiagnosticEngine(repo.Workspace).Analyze(Fixtures.Parse("missing-package-cs0246.txt"));

        Assert.Equal(MissingPackageReason.MissingUsing, analysis.MissingPackages.Single(m => m.Package == "Newtonsoft.Json").Reason);
    }

    [Fact]
    public void Infers_the_project_when_the_log_omits_it()
    {
        using var repo = new TempRepo();
        // Terminal logger output has no "[project.csproj]" suffix.
        var log = LogParser.ParseText(
            "    src/Shop.Api/Services/OrderSerializer.cs(3,7): error CS0246: The type or namespace name 'Newtonsoft' could not be found (are you missing a using directive or an assembly reference?)");

        var missing = Assert.Single(new LocalDiagnosticEngine(repo.Workspace).Analyze(log).MissingPackages);

        Assert.Equal("src/Shop.Api/Shop.Api.csproj", missing.Project);
    }

    [Fact]
    public void Downstream_CS0006_is_flagged_as_symptom_and_ranked_last()
    {
        var analysis = new LocalDiagnosticEngine().Analyze(Fixtures.Parse("missing-package-cs0246.txt"));

        var last = analysis.Hints[^1];
        Assert.Equal("CS0006", last.Code);
        Assert.True(last.IsLikelySymptom);
        Assert.Equal(2, analysis.Hints.Single(h => h.Code == "CS0103").Occurrences);
    }

    [Fact]
    public void Linq_methods_suggest_using_System_Linq()
    {
        var log = LogParser.ParseText(
            "/r/A.cs(9,22): error CS1061: 'List<int>' does not contain a definition for 'Where' and no accessible extension method 'Where' accepting a first argument of type 'List<int>' could be found (are you missing a using directive or an assembly reference?) [/r/A.csproj]");

        var hint = Assert.Single(new LocalDiagnosticEngine().Analyze(log).Hints);
        Assert.Contains(hint.Suggestions, s => s.Contains("using System.Linq;", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_problems_produce_missing_packages_and_version_conflicts_first()
    {
        var analysis = new LocalDiagnosticEngine().Analyze(Fixtures.Parse("nuget-restore.txt"));

        var missing = Assert.Single(analysis.MissingPackages);
        Assert.Equal("Contoso.Payments.Sdk", missing.Package);
        Assert.Equal(MissingPackageReason.NotFoundInFeeds, missing.Reason);

        var downgrade = analysis.VersionConflicts.Single(c => c.Code == "NU1605");
        Assert.Equal("Microsoft.Extensions.Logging.Abstractions", downgrade.Name);
        Assert.Equal(["9.0.0", "8.0.0"], downgrade.Versions);
        Assert.Equal("9.0.0", downgrade.RecommendedVersion);
        Assert.Equal("dotnet add /src/app/src/Billing/Billing.csproj package Microsoft.Extensions.Logging.Abstractions --version 9.0.0", downgrade.SuggestedCommand);

        var polly = analysis.VersionConflicts.Single(c => c.Code == "NU1608");
        Assert.Equal("Polly", polly.Name);
        Assert.Null(polly.RecommendedVersion); // 8.4.2 is the version that violates the constraint
        Assert.Equal("upgrade Polly.Extensions.Http to a version that supports Polly 8.4.2, or pin Polly to >= 7.1.0 && < 8.0.0", polly.SuggestedCommand);
        Assert.Equal("Polly.Extensions.Http 3.0.0 requires Polly >= 7.1.0 && < 8.0.0, but 8.4.2 was resolved.", analysis.Hints.Single(h => h.Code == "NU1608").Summary);

        var sdk = analysis.Hints.Single(h => h.Code == "NETSDK1045");
        Assert.Contains(".NET 10.0", sdk.Summary);
        Assert.Contains(sdk.Suggestions, s => s.Contains("dotnet-version: '10.0.x'", StringComparison.Ordinal));
        Assert.Equal(["/src/app/src/Worker/Worker.csproj"], sdk.Locations);

        Assert.All(analysis.Hints.TakeWhile(h => h.Severity == DiagnosticSeverity.Error), h => Assert.Equal(0, h.Priority));
    }

    [Fact]
    public void Multiple_feeds_with_central_package_management_suggest_a_repo_nuget_config()
    {
        // Real output from a Windows machine with a company feed configured globally.
        using var repo = new TempRepo();
        var log = Fixtures.Parse("nu1507-multiple-sources.txt");
        var hint = Assert.Single(new LocalDiagnosticEngine(repo.Workspace).Analyze(log).Hints);

        Assert.Equal(2, log.ErrorCount);
        Assert.Equal("NU1507", hint.Code);
        Assert.Equal(2, hint.Occurrences);
        Assert.Contains(hint.Suggestions, s => s.Contains("nuget.config", StringComparison.Ordinal) && s.Contains("<clear />", StringComparison.Ordinal));
    }

    [Fact]
    public void Assembly_version_conflicts_recommend_the_higher_version()
    {
        var analysis = new LocalDiagnosticEngine().Analyze(Fixtures.Parse("version-conflict.txt"));

        var cs1705 = analysis.VersionConflicts.Single(c => c.Code == "CS1705");
        Assert.Equal("System.Text.Json", cs1705.Name);
        Assert.Equal("8.0.0.0", cs1705.RecommendedVersion);
        Assert.EndsWith("package System.Text.Json --version 8.0.0", cs1705.SuggestedCommand);

        var msb3277 = analysis.VersionConflicts.Single(c => c.Code == "MSB3277");
        Assert.Equal(["6.0.0.0", "8.0.0.0"], msb3277.Versions);

        Assert.Contains("GenerateDepsFile", analysis.Hints.Single(h => h.Code == "MSB4018").Summary);
        Assert.Equal("Could not copy file after retries", analysis.Hints.Single(h => h.Code == "MSB3027").Title);
    }

    [Fact]
    public void Syntax_errors_point_at_their_location()
    {
        var analysis = new LocalDiagnosticEngine().Analyze(Fixtures.Parse("syntax-errors.txt"));

        var hint = analysis.Hints.Single(h => h.Code == "CS1002");
        Assert.Equal("; expected", hint.Title);
        Assert.Equal("Syntax error at /src/calc/src/Calculator/MathEngine.cs:12:35.", hint.Summary);
        Assert.Equal(["/src/calc/src/Calculator/MathEngine.cs:12:35"], hint.Locations);
    }

    [Fact]
    public void Test_failures_get_targeted_hints()
    {
        var hints = new LocalDiagnosticEngine().Analyze(Fixtures.Parse("dotnet-test-vstest.txt")).Hints;

        var assertion = hints.Single(h => h.Title.StartsWith("Assertion failed", StringComparison.Ordinal));
        Assert.Equal("Expected 2.5 but got 2 (/src/calc/tests/Calculator.Tests/MathEngineTests.cs:21).", assertion.Summary);

        var nre = hints.Single(h => h.Code == "NullReferenceException");
        Assert.Contains("/src/calc/src/Calculator/History.cs:34", nre.Summary);

        var generic = hints.Single(h => h.Code == "InvalidOperationException");
        Assert.Contains("Unexpected token", generic.Summary);
    }

    [Fact]
    public void Failed_build_without_diagnostics_still_gets_guidance()
    {
        var log = LogParser.ParseText("  App -> /src/App.dll\nBuild FAILED.\n");

        var hint = Assert.Single(new LocalDiagnosticEngine().Analyze(log).Hints);
        Assert.Equal("BUILD", hint.Code);
        Assert.Contains(hint.Suggestions, s => s.Contains("-bl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("JsonConvert", "Newtonsoft.Json")]
    [InlineData("Newtonsoft", "Newtonsoft.Json")]
    [InlineData("Newtonsoft.Json.Linq", "Newtonsoft.Json")]
    [InlineData("ILogger<T>", "Microsoft.Extensions.Logging.Abstractions")]
    [InlineData("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore")]
    [InlineData("Xunit", "xunit")]
    [InlineData("Microsoft", null)]
    [InlineData("MyCompany.Internal", null)]
    public void Maps_well_known_names_to_packages(string name, string? package)
    {
        Assert.Equal(package, WellKnownPackages.FindPackageForName(name));
    }

    [Fact]
    public void Catalog_covers_the_common_codes_with_docs_links()
    {
        foreach (var code in new[] { "CS0246", "CS1061", "MSB4018", "MSB3277", "NU1101", "NU1605", "NETSDK1045" })
        {
            var entry = KnownErrorCatalog.Find(code);
            Assert.NotNull(entry);
            Assert.NotEmpty(entry.Fixes);
            Assert.StartsWith("https://learn.microsoft.com/", entry.DocsUrl);
        }

        Assert.Equal("https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1101", KnownErrorCatalog.DocsUrlFor("NU1101"));
        Assert.Null(KnownErrorCatalog.Find("CS9999"));
    }
}
