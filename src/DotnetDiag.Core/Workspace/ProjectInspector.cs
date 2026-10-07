using System.Xml;
using System.Xml.Linq;

namespace DotnetDiag.Core.Workspace;

public sealed record PackageReferenceInfo(string Name, string? Version, string Source);

/// <summary>
/// Reads PackageReference items from a project file plus central versions from Directory.Packages.props,
/// so hints can tell "package missing" apart from "package present but namespace not imported".
/// </summary>
public static class ProjectInspector
{
    public static IReadOnlyList<PackageReferenceInfo> GetPackageReferences(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return [];
        }

        var central = FindCentralVersions(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);
        var result = new List<PackageReferenceInfo>();
        foreach (var element in LoadItems(projectPath, "PackageReference"))
        {
            var name = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var version = (string?)element.Attribute("Version")
                ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                ?? (string?)element.Attribute("VersionOverride")
                ?? central.GetValueOrDefault(name);
            result.Add(new PackageReferenceInfo(name, version, Path.GetFileName(projectPath)));
        }

        return result;
    }

    /// <summary>
    /// The nearest project file above <paramref name="sourceFile"/>, for logs that omit the project
    /// (e.g. the terminal logger). Stops at <paramref name="stopAt"/> when given.
    /// </summary>
    public static string? FindOwningProject(string sourceFile, string? stopAt = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(sourceFile));
        var stop = stopAt is null ? null : Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrEmpty(dir))
        {
            try
            {
                var projects = Directory.GetFiles(dir, "*.*proj");
                if (projects.Length == 1)
                {
                    return projects[0];
                }

                if (projects.Length > 1)
                {
                    return null; // ambiguous
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (stop is not null && string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), stop, StringComparison.Ordinal))
            {
                return null;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static Dictionary<string, string> FindCentralVersions(string startDirectory)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var props = Path.Combine(dir.FullName, "Directory.Packages.props");
            if (File.Exists(props))
            {
                foreach (var element in LoadItems(props, "PackageVersion"))
                {
                    var name = (string?)element.Attribute("Include");
                    var version = (string?)element.Attribute("Version");
                    if (name is not null && version is not null)
                    {
                        versions.TryAdd(name, version);
                    }
                }

                break;
            }

            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                break;
            }

            dir = dir.Parent;
        }

        return versions;
    }

    private static IEnumerable<XElement> LoadItems(string path, string itemName)
    {
        try
        {
            var doc = XDocument.Load(path);
            return doc.Descendants().Where(e => e.Name.LocalName == itemName).ToArray();
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
