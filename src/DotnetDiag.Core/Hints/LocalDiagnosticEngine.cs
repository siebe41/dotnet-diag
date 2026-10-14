using System.Text.RegularExpressions;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Parsing;
using DotnetDiag.Core.Workspace;

namespace DotnetDiag.Core.Hints;

/// <summary>
/// Produces structured, offline diagnostic hints. This is what users get without any LLM configured, and it
/// is also handed to the LLM as a starting point.
/// </summary>
public sealed partial class LocalDiagnosticEngine(GitWorkspace? workspace = null)
{
    private const int MaxLocations = 5;
    private const int MaxTestHints = 5;

    private static readonly HashSet<string> SyntaxErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS1002", "CS1003", "CS1022", "CS1026", "CS1513", "CS1514", "CS1519", "CS1525", "CS1529", "CS1733", "CS8124",
    };

    /// <summary>Warnings that are worth a hint because they often explain errors or runtime failures.</summary>
    private static readonly HashSet<string> NotableWarnings = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSB3245", "MSB3277", "NU1603", "NU1605", "NU1608", "NU1701", "NU1902", "NU1903", "NU1904", "NETSDK1138", "CS1701", "CS1702",
    };

    private readonly Dictionary<string, IReadOnlyList<PackageReferenceInfo>> _packageCache = new(StringComparer.OrdinalIgnoreCase);

    public LocalAnalysis Analyze(ParsedLog log)
    {
        var hints = new List<LocalHint>();
        var missing = new List<MissingPackage>();
        var conflicts = new List<VersionConflict>();

        var groups = log.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || NotableWarnings.Contains(d.Code))
            .GroupBy(d => (d.Code, d.Severity));

        foreach (var group in groups)
        {
            var items = group.ToList();
            hints.Add(BuildHint(group.Key.Code, group.Key.Severity, items, missing, conflicts));
        }

        hints.AddRange(log.TestFailures.Take(MaxTestHints).Select(f => BuildTestHint(f, conflicts)));

        if (hints.Count == 0 && log.BuildSucceeded == false)
        {
            hints.Add(new LocalHint(
                "BUILD",
                DiagnosticSeverity.Error,
                "Build failed without a recognizable diagnostic",
                "The log reports a failed build but contains no error lines in MSBuild's canonical format.",
                [
                    "Re-run with a binary log for full detail: `dotnet build -bl` then `dotnet-diag analyze msbuild.binlog`.",
                    "Increase verbosity: `dotnet build -v:n` and look for the first `error` line.",
                ],
                [],
                1,
                Priority: 0));
        }

        if (log.TestSummary is { Failed: > 0 } summary && log.TestFailures.Count == 0)
        {
            hints.Add(new LocalHint(
                "TEST",
                DiagnosticSeverity.Error,
                $"{summary.Failed} test(s) failed but no failure details were captured",
                "The test summary reports failures but the output does not include per-test details.",
                ["Re-run with detailed console output: `dotnet test --logger \"console;verbosity=normal\"`."],
                [],
                summary.Failed,
                Priority: 3));
        }

        return new LocalAnalysis(
            hints.OrderBy(h => h.Priority).ThenByDescending(h => h.Severity).ToArray(),
            missing.DistinctBy(m => (m.Package.ToLowerInvariant(), m.Reason)).ToArray(),
            conflicts.DistinctBy(c => (c.Name.ToLowerInvariant(), c.Code)).ToArray());
    }

    private LocalHint BuildHint(
        string code,
        DiagnosticSeverity severity,
        IReadOnlyList<BuildDiagnostic> items,
        List<MissingPackage> missing,
        List<VersionConflict> conflicts)
    {
        var known = KnownErrorCatalog.Find(code);
        var first = items[0];
        var suggestions = new List<string>();
        string? summary = null;
        var symptom = false;

        switch (code.ToUpperInvariant())
        {
            case "CS0246" or "CS0234" or "CS0103":
                summary = EnrichMissingName(code, items, suggestions, missing);
                break;

            case "CS1061":
                summary = EnrichMissingMember(items, suggestions, missing);
                break;

            case "CS0012":
                foreach (var d in items)
                {
                    var asm = AssemblyReference().Match(d.Message);
                    if (asm.Success)
                    {
                        var name = asm.Groups["name"].Value;
                        missing.Add(new MissingPackage(name, MissingPackageReason.MissingTransitiveReference, d.Message, ProjectName(d)));
                        suggestions.Add($"Reference `{name}` directly: `dotnet add {ProjectName(d) ?? "<project>"} package {name}` (or add the ProjectReference).");
                    }
                }

                break;

            case "CS1705":
                foreach (var d in items)
                {
                    var m = HigherAssemblyVersion().Match(d.Message);
                    if (m.Success)
                    {
                        var conflict = new VersionConflict(m.Groups["name"].Value, code, d.Message, [m.Groups["high"].Value, m.Groups["low"].Value], ProjectName(d));
                        conflicts.Add(conflict);
                        summary ??= $"{conflict.Name} is referenced at {m.Groups["low"].Value} but a dependency needs {m.Groups["high"].Value}.";
                        suggestions.Add($"`{conflict.SuggestedCommand}`");
                    }
                }

                break;

            case "MSB3277":
                foreach (var d in items)
                {
                    var name = Patterns.DoubleQuotedName().Match(d.Message);
                    if (!name.Success)
                    {
                        continue;
                    }

                    var versions = AssemblyVersion().Matches(d.Message).Select(v => v.Groups["v"].Value).Distinct().ToArray();
                    var conflict = new VersionConflict(name.Groups["name"].Value, code, d.Message, versions, ProjectName(d));
                    conflicts.Add(conflict);
                    summary ??= $"MSBuild could not unify the versions of {conflict.Name} referenced by this project's dependencies.";
                    suggestions.Add(versions.Length > 0
                        ? $"Pin the highest version explicitly: `{conflict.SuggestedCommand}`."
                        : $"Find who brings which version: `dotnet list package --include-transitive` and look for {conflict.Name}.");
                }

                break;

            case "NU1605":
                foreach (var d in items)
                {
                    var m = PackageDowngrade().Match(d.Message);
                    if (m.Success)
                    {
                        var conflict = new VersionConflict(m.Groups["name"].Value, code, d.Message, [m.Groups["high"].Value, m.Groups["low"].Value], ProjectName(d));
                        conflicts.Add(conflict);
                        summary ??= $"{conflict.Name} is pinned to {m.Groups["low"].Value} but a dependency requires {m.Groups["high"].Value}.";
                        suggestions.Add($"Raise the direct reference: `{conflict.SuggestedCommand}`.");
                    }
                }

                break;

            case "NU1107":
                foreach (var d in items)
                {
                    var m = DirectReferenceAdvice().Match(d.Message);
                    if (m.Success)
                    {
                        var conflict = new VersionConflict(m.Groups["name"].Value, code, d.Message, [m.Groups["version"].Value], ProjectName(d));
                        conflicts.Add(conflict);
                        suggestions.Add($"`{conflict.SuggestedCommand}`");
                    }
                }

                break;

            case "NU1608":
                foreach (var d in items)
                {
                    var m = OutsideConstraint().Match(d.Message);
                    if (m.Success)
                    {
                        var name = m.Groups["name"].Value;
                        var owner = m.Groups["owner"].Value;
                        var range = m.Groups["range"].Value;
                        var ownerId = owner.Split(' ')[0];
                        conflicts.Add(new VersionConflict(name, code, d.Message, [m.Groups["resolved"].Value], ProjectName(d),
                            Advice: $"upgrade {ownerId} to a version that supports {name} {m.Groups["resolved"].Value}, or pin {name} to {range}"));
                        summary ??= $"{owner} requires {name} {range}, but {m.Groups["resolved"].Value} was resolved.";
                        suggestions.Add($"Upgrade {ownerId} to a release compatible with {name} {m.Groups["resolved"].Value} (`dotnet package search {ownerId}`), or constrain {name} to {range}.");
                    }
                }

                break;

            case "NU1101" or "NU1102" or "NU1103":
                foreach (var d in items)
                {
                    var m = UnableToFindPackage().Match(d.Message);
                    if (m.Success)
                    {
                        var version = m.Groups["version"].Success ? m.Groups["version"].Value : null;
                        missing.Add(new MissingPackage(m.Groups["name"].Value, MissingPackageReason.NotFoundInFeeds, d.Message, ProjectName(d), version));
                        summary ??= version is null
                            ? $"Restore cannot find package '{m.Groups["name"].Value}' in any configured source."
                            : $"Restore cannot find '{m.Groups["name"].Value}' {version} in any configured source.";
                    }
                }

                suggestions.Add("Restore failures cascade into compiler errors (CS0246 etc.): fix these first.");
                break;

            case "NETSDK1045":
                var target = SdkTarget().Match(first.Message);
                if (target.Success)
                {
                    var version = target.Groups["version"].Value;
                    summary = $"The installed SDK cannot build .NET {version} projects.";
                    suggestions.Add($"Install the .NET {version} SDK (https://dot.net/download), or in GitHub Actions set `dotnet-version: '{version}.x'` on actions/setup-dotnet.");
                }

                break;

            case "MSB4018":
                var task = Patterns.DoubleQuotedName().Match(first.Message);
                if (task.Success)
                {
                    summary = $"The MSBuild task \"{task.Groups["name"].Value}\" threw an exception.";
                }

                break;

            case "CS0006":
                symptom = true;
                summary = "A referenced project failed to produce its assembly — fix that project's errors first.";
                break;
        }

        if (SyntaxErrorCodes.Contains(code))
        {
            summary ??= $"Syntax error at {Location(first)}.";
            if (items.Count > 1 || items.Select(i => i.File).Distinct().Count() == 1)
            {
                suggestions.Add("Fix the first syntax error in each file first; later ones are often caused by it.");
            }
        }

        if (known is not null)
        {
            foreach (var fix in known.Fixes)
            {
                if (!suggestions.Contains(fix))
                {
                    suggestions.Add(fix);
                }
            }
        }
        else if (suggestions.Count == 0)
        {
            suggestions.Add($"Search the docs for {code}: {KnownErrorCatalog.DocsUrlFor(code)}");
        }

        var title = known?.Title ?? TextSanitizer.Truncate(first.Message, 80);
        summary ??= known?.Explanation ?? first.Message;

        return new LocalHint(
            code,
            severity,
            title,
            summary,
            suggestions.Distinct().Take(6).ToArray(),
            items.Select(Location).Where(l => l.Length > 0).Distinct().Take(MaxLocations).ToArray(),
            items.Count,
            Priority(code, severity, symptom),
            KnownErrorCatalog.DocsUrlFor(code),
            symptom);
    }

    private string? EnrichMissingName(string code, IReadOnlyList<BuildDiagnostic> items, List<string> suggestions, List<MissingPackage> missing)
    {
        string? summary = null;
        foreach (var d in items)
        {
            var names = Patterns.QuotedName().Matches(d.Message).Select(m => m.Groups["name"].Value).ToArray();
            if (names.Length == 0)
            {
                continue;
            }

            // CS0234: 'Json' does not exist in the namespace 'Newtonsoft' → Newtonsoft.Json
            var candidate = code.Equals("CS0234", StringComparison.OrdinalIgnoreCase) && names.Length >= 2
                ? $"{names[1]}.{names[0]}"
                : names[0];

            var package = WellKnownPackages.FindPackageForName(candidate)
                ?? (names.Length >= 2 ? WellKnownPackages.FindPackageForName(names[1]) : null);
            summary ??= $"'{candidate}' cannot be resolved{(d.File is null ? string.Empty : $" in {Location(d)}")}.";
            if (package is null)
            {
                continue;
            }

            var project = ProjectName(d);
            var referenced = IsPackageReferenced(d, package);
            if (referenced)
            {
                missing.Add(new MissingPackage(package, MissingPackageReason.MissingUsing, d.Message, project));
                suggestions.Add($"{package} is already referenced by {project}: add the matching `using` directive, then `dotnet restore`.");
            }
            else
            {
                var add = NotReferenced(package, d);
                missing.Add(add);
                suggestions.Add($"'{candidate}' comes from the {package} package: `{CommandFor(add)}`.");
            }
        }

        return summary;
    }

    private string? EnrichMissingMember(IReadOnlyList<BuildDiagnostic> items, List<string> suggestions, List<MissingPackage> missing)
    {
        string? summary = null;
        foreach (var d in items)
        {
            var m = MissingMember().Match(d.Message);
            if (!m.Success)
            {
                continue;
            }

            var type = m.Groups["type"].Value;
            var member = m.Groups["member"].Value;
            summary ??= $"'{type}' has no member '{member}' at {Location(d)}.";

            if (WellKnownPackages.FindExtensionMethod(member) is { } extension)
            {
                if (extension.Package is null)
                {
                    suggestions.Add($"`{member}` is a LINQ extension method: add `using {extension.Namespace};` (or enable ImplicitUsings).");
                }
                else if (IsPackageReferenced(d, extension.Package))
                {
                    suggestions.Add($"`{member}` comes from {extension.Package} (referenced): add `using {extension.Namespace};`.");
                }
                else
                {
                    var add = NotReferenced(extension.Package, d);
                    missing.Add(add);
                    suggestions.Add($"`{member}` is an extension method from {extension.Package}: `{CommandFor(add)}`.");
                }
            }
            else
            {
                suggestions.Add($"Check that `{member}` exists on `{type}` in the version of the library you reference (it may have been renamed).");
            }
        }

        return summary;
    }

    private LocalHint BuildTestHint(TestFailure failure, List<VersionConflict> conflicts)
    {
        var frame = failure.FirstUserFrame;
        var where = frame is null
            ? null
            : frame.File is null ? frame.Method : $"{Relative(frame.File)}:{frame.Line}";
        var shortName = ShortTestName(failure.TestName);
        var message = failure.ErrorMessage;
        var exception = failure.ExceptionType;
        var suggestions = new List<string>();
        string title;
        string summary;

        if (message.Contains("Could not load file or assembly", StringComparison.OrdinalIgnoreCase))
        {
            var asm = Patterns.QuotedName().Match(message);
            var name = asm.Success ? asm.Groups["name"].Value.Split(',')[0] : "the assembly";
            title = $"Assembly load failure in {shortName}";
            summary = $"The test host could not load {name}; this is usually a package version conflict.";
            var versions = AssemblyVersion().Matches(message).Select(v => v.Groups["v"].Value).Distinct().ToArray();
            if (asm.Success)
            {
                conflicts.Add(new VersionConflict(name, "TEST", TextSanitizer.Truncate(message, 300), versions, null));
            }

            suggestions.Add($"Align {name} versions across projects (`dotnet list package --include-transitive`).");
            suggestions.Add("Clean bin/obj of the test project to remove stale copies.");
        }
        else if (exception?.EndsWith("NullReferenceException", StringComparison.Ordinal) == true)
        {
            title = $"Null dereference in {shortName}";
            summary = where is null ? "A null value was dereferenced." : $"A null value was dereferenced at {where}.";
            suggestions.Add("Check which object on that line can be null (uninitialized dependency, missing mock setup, empty query result).");
        }
        else if (message.Contains("No service for type", StringComparison.Ordinal) ||
                 message.Contains("Unable to resolve service for type", StringComparison.Ordinal))
        {
            var service = Patterns.QuotedName().Match(message);
            title = $"Missing DI registration in {shortName}";
            summary = service.Success ? $"'{service.Groups["name"].Value}' is not registered in the service container." : "A dependency is not registered in the service container.";
            suggestions.Add("Register the service (e.g. `services.AddScoped<IFoo, Foo>()`) in the test host or WebApplicationFactory setup.");
        }
        else if (exception?.EndsWith("FileNotFoundException", StringComparison.Ordinal) == true ||
                 exception?.EndsWith("DirectoryNotFoundException", StringComparison.Ordinal) == true)
        {
            title = $"Missing file in {shortName}";
            summary = "The test expected a file that is not present in the output directory.";
            suggestions.Add("Mark fixture files with `<None Include=\"...\" CopyToOutputDirectory=\"PreserveNewest\" />`.");
            suggestions.Add("Build paths from `AppContext.BaseDirectory` instead of the working directory.");
        }
        else if (exception?.Contains("Timeout", StringComparison.Ordinal) == true ||
                 message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("exceeded timeout", StringComparison.OrdinalIgnoreCase))
        {
            title = $"Timeout in {shortName}";
            summary = "The test did not finish in time.";
            suggestions.Add("Look for blocking calls on async code (`.Result`, `.Wait()`), missing `await`, or unavailable external services.");
        }
        else if (message.Contains("Expected", StringComparison.OrdinalIgnoreCase) &&
                 message.Contains("Actual", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("Assert.", StringComparison.Ordinal))
        {
            title = $"Assertion failed: {shortName}";
            var (expected, actual) = ExtractExpectedActual(message);
            summary = expected is not null && actual is not null
                ? $"Expected {expected} but got {actual}{(where is null ? string.Empty : $" ({where})")}."
                : $"An assertion failed{(where is null ? string.Empty : $" at {where}")}.";
            suggestions.Add("Decide whether the production code regressed or the expectation is outdated.");
            if (frame is not null && !IsTestMethodFrame(frame, failure.TestName))
            {
                suggestions.Add($"Inspect recent changes to `{ShortTestName(frame.Method)}`, which the test exercises.");
            }
        }
        else
        {
            title = exception is null ? $"Test failure: {shortName}" : $"{shortName} threw";
            summary = TextSanitizer.Truncate(TextSanitizer.CollapseWhitespace(message), 200);
            if (summary.Length == 0)
            {
                summary = "The test failed without an error message.";
            }
        }

        if (where is not null)
        {
            suggestions.Add($"Start at {where}.");
        }

        return new LocalHint(
            exception is null ? "TEST" : ExceptionName(exception),
            DiagnosticSeverity.Error,
            title,
            summary,
            suggestions,
            where is null ? [] : [where],
            1,
            Priority: 3);
    }

    private static (string? Expected, string? Actual) ExtractExpectedActual(string message)
    {
        string? expected = null;
        string? actual = null;
        foreach (var line in message.Split('\n'))
        {
            var t = line.Trim();
            if (expected is null && t.StartsWith("Expected:", StringComparison.OrdinalIgnoreCase))
            {
                expected = t["Expected:".Length..].Trim();
            }
            else if (actual is null && (t.StartsWith("Actual:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("But was:", StringComparison.OrdinalIgnoreCase)))
            {
                actual = t[(t.IndexOf(':') + 1)..].Trim();
            }
        }

        return (expected is null ? null : TextSanitizer.Truncate(expected, 60), actual is null ? null : TextSanitizer.Truncate(actual, 60));
    }

    private static int Priority(string code, DiagnosticSeverity severity, bool symptom)
    {
        if (symptom)
        {
            return 9;
        }

        if (severity != DiagnosticSeverity.Error)
        {
            return 5;
        }

        return DiagnosticCategories.FromCode(code) switch
        {
            DiagnosticCategory.DotnetSdk or DiagnosticCategory.NuGet => 0,
            DiagnosticCategory.MSBuild => 1,
            _ => 2,
        };
    }

    private bool IsPackageReferenced(BuildDiagnostic diagnostic, string package) =>
        PackageReferences(diagnostic).Any(r => r.Name.Equals(package, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<PackageReferenceInfo> PackageReferences(BuildDiagnostic diagnostic)
    {
        var projectPath = ProjectPath(diagnostic);
        var resolved = projectPath is null ? null : PathConfinement.ResolveFromLog(projectPath, workspace);
        if (resolved is null)
        {
            return [];
        }

        if (!_packageCache.TryGetValue(resolved, out var references))
        {
            references = ProjectInspector.GetPackageReferences(resolved, PathConfinement.RootFor(workspace));
            _packageCache[resolved] = references;
        }

        return references;
    }

    private MissingPackage NotReferenced(string package, BuildDiagnostic diagnostic) =>
        new(package, MissingPackageReason.NotReferenced, diagnostic.Message, ProjectName(diagnostic), AlignedVersion(package, diagnostic));

    private static string CommandFor(MissingPackage package) =>
        package.Project is null ? package.SuggestedCommand.Replace("dotnet add package", "dotnet add <project> package", StringComparison.Ordinal) : package.SuggestedCommand;

    /// <summary>
    /// Microsoft.Extensions.*, EF Core and ASP.NET Core packages ship in lockstep with each .NET release
    /// (EF Core 10 needs Microsoft.Extensions.* 10). Adding the latest version next to older release-train
    /// packages causes NU1605 downgrades, so match the version the project already uses for that train.
    /// </summary>
    private string? AlignedVersion(string package, BuildDiagnostic diagnostic)
    {
        static bool OnReleaseTrain(string name) =>
            ReleaseTrainPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (!OnReleaseTrain(package))
        {
            return null;
        }

        return PackageReferences(diagnostic)
            .Where(r => OnReleaseTrain(r.Name) && r.Version is not null)
            .Select(r => (Raw: r.Version!, Parsed: Version.TryParse(r.Version!.Split('-')[0], out var v) ? v : null))
            .Where(r => r.Parsed is not null)
            .OrderByDescending(r => r.Parsed)
            .Select(r => r.Raw)
            .FirstOrDefault();
    }

    private static readonly string[] ReleaseTrainPrefixes = ["Microsoft.Extensions.", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore."];

    private string Location(BuildDiagnostic diagnostic) => diagnostic.DisplayLocation(Relative);

    private string Relative(string path) => workspace?.ToRelative(path) ?? path.Replace('\\', '/');

    private string? ProjectName(BuildDiagnostic diagnostic) =>
        ProjectPath(diagnostic) is { } project ? Relative(project) : null;

    /// <summary>The diagnostic's project, or the project that owns its source file when the log omits it.</summary>
    private string? ProjectPath(BuildDiagnostic diagnostic)
    {
        if (diagnostic.Project is not null || diagnostic.File is null || workspace is null)
        {
            return diagnostic.Project;
        }

        var file = workspace.Resolve(diagnostic.File);
        return file is null ? null : ProjectInspector.FindOwningProject(file, workspace.Root);
    }

    private static string ExceptionName(string exceptionType) => exceptionType.Split('.')[^1];

    private static bool IsTestMethodFrame(StackFrameInfo frame, string testName)
    {
        static string Strip(string name) => name.IndexOf('(') is var i and > 0 ? name[..i] : name;
        return Strip(frame.Method).Equals(Strip(testName), StringComparison.Ordinal);
    }

    public static string ShortTestName(string name)
    {
        var paren = name.IndexOf('(');
        var core = paren > 0 ? name[..paren] : name;
        var parts = core.Split('.');
        var shortName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : core;
        return paren > 0 ? shortName + "(…)" : shortName;
    }

    [GeneratedRegex(@"'(?<type>[^']+)' does not contain a definition for '(?<member>[^']+)'")]
    private static partial Regex MissingMember();

    [GeneratedRegex(@"reference to assembly '(?<name>[^,']+)")]
    private static partial Regex AssemblyReference();

    [GeneratedRegex(@"uses '(?<name>[^,']+), Version=(?<high>[\d.]+)[^']*' which has a higher version than referenced assembly '[^']+' with identity '[^,']+, Version=(?<low>[\d.]+)")]
    private static partial Regex HigherAssemblyVersion();

    [GeneratedRegex(@"Version=(?<v>\d+(?:\.\d+){1,3})")]
    private static partial Regex AssemblyVersion();

    [GeneratedRegex(@"downgrade:\s+(?<name>\S+)\s+from\s+(?<high>[^\s]+?)\s+to\s+(?<low>[^\s]+?)\.?(?:\s|$)")]
    private static partial Regex PackageDowngrade();

    [GeneratedRegex(@"reference\s+(?<name>\S+)\s+(?<version>\d[^\s]*)\s+directly")]
    private static partial Regex DirectReferenceAdvice();

    [GeneratedRegex(@"(?<owner>\S+\s+\S+)\s+requires\s+(?<name>\S+)\s+\((?<range>[^)]+)\)\s+but version\s+\S+\s+(?<resolved>[^\s]+?)\s+was resolved")]
    private static partial Regex OutsideConstraint();

    [GeneratedRegex(@"Unable to find (?:a stable )?package\s+'?(?<name>[A-Za-z0-9_.\-]+?)'?(?:\.?\s+with version\s+\((?<version>[^)]+)\)|\.(?:\s|$)|\s|$)")]
    private static partial Regex UnableToFindPackage();

    [GeneratedRegex(@"does not support targeting \.NET (?<version>\d+(?:\.\d+)?)")]
    private static partial Regex SdkTarget();
}
