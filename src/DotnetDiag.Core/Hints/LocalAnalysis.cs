using DotnetDiag.Core.Models;

namespace DotnetDiag.Core.Hints;

/// <summary>An actionable, offline explanation for one diagnostic code or failing test.</summary>
public sealed record LocalHint(
    string Code,
    DiagnosticSeverity Severity,
    string Title,
    string Summary,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> Locations,
    int Occurrences,
    int Priority,
    string? DocsUrl = null,
    bool IsLikelySymptom = false);

public enum MissingPackageReason
{
    /// <summary>Code uses a type from a package the project does not reference.</summary>
    NotReferenced,

    /// <summary>The package is referenced but the namespace is not imported.</summary>
    MissingUsing,

    /// <summary>The project references the package but restore could not find it (NU1101/NU1102).</summary>
    NotFoundInFeeds,

    /// <summary>A transitive assembly is needed but not referenced (CS0012).</summary>
    MissingTransitiveReference,
}

public sealed record MissingPackage(string Package, MissingPackageReason Reason, string Evidence, string? Project, string? Version = null)
{
    public string SuggestedCommand => Reason switch
    {
        MissingPackageReason.MissingUsing => $"add `using` for {Package} types (package is already referenced)",
        MissingPackageReason.NotFoundInFeeds => "dotnet nuget list source  # check feeds/credentials",
        _ => (Project is null ? $"dotnet add package {Package}" : $"dotnet add {Project} package {Package}")
             + (Version is null ? string.Empty : $" --version {Version}"),
    };
}

public sealed record VersionConflict(string Name, string Code, string Detail, IReadOnlyList<string> Versions, string? Project, string? Advice = null)
{
    /// <summary>
    /// The highest version mentioned, which is usually the one to pin explicitly. Null when pinning is not the
    /// fix (e.g. NU1608, where the resolved version is the one that violates a constraint).
    /// </summary>
    public string? RecommendedVersion => Advice is not null ? null :
        Versions
            .Select(v => (Raw: v, Parsed: Version.TryParse(v.Split('-')[0], out var p) ? p : null))
            .Where(v => v.Parsed is not null)
            .OrderByDescending(v => v.Parsed)
            .Select(v => v.Raw)
            .FirstOrDefault();

    public string SuggestedCommand
    {
        get
        {
            if (Advice is not null)
            {
                return Advice;
            }

            var target = Project is null ? string.Empty : Project + " ";
            return RecommendedVersion is { } version
                ? $"dotnet add {target}package {Name} --version {TrimAssemblyVersion(version)}"
                : $"dotnet list {target}package --include-transitive | grep {Name}";
        }
    }

    private static string TrimAssemblyVersion(string version) =>
        version.EndsWith(".0", StringComparison.Ordinal) && version.Count(c => c == '.') == 3 ? version[..^2] : version;
}

/// <summary>Result of the offline analysis.</summary>
public sealed record LocalAnalysis(
    IReadOnlyList<LocalHint> Hints,
    IReadOnlyList<MissingPackage> MissingPackages,
    IReadOnlyList<VersionConflict> VersionConflicts);
