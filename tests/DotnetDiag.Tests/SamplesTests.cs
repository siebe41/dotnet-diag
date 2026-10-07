using System.Text.Json;
using DotnetDiag.Core.Parsing;

namespace DotnetDiag.Tests;

/// <summary>The committed samples/ logs must keep producing the diagnosis their scenario expects.</summary>
public class SamplesTests
{
    public static TheoryData<string, string> SampleFiles()
    {
        var data = new TheoryData<string, string>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "samples")).Where(f => f.EndsWith(".binlog", StringComparison.Ordinal) || f.EndsWith(".txt", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(file), Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Sample_is_diagnosed_as_its_scenario_expects(string scenario, string file)
    {
        var scenarioJson = Path.Combine(RepoRoot, "eval", "scenarios", scenario, "scenario.json");
        Assert.True(File.Exists(scenarioJson), $"samples/{file} has no matching eval/scenarios/{scenario}");
        using var json = JsonDocument.Parse(File.ReadAllText(scenarioJson));
        var root = json.RootElement;
        if (root.TryGetProperty("knownGap", out _))
        {
            return; // documented as not handled yet
        }

        var expect = root.GetProperty("expect");
        var log = LogParser.ParseFile(Path.Combine(RepoRoot, "samples", file));

        if (expect.TryGetProperty("codes", out var codes))
        {
            foreach (var code in codes.EnumerateArray().Select(c => c.GetString()!))
            {
                Assert.Contains(log.Diagnostics, d => d.Code == code);
            }
        }

        if (expect.TryGetProperty("testFailures", out var failures))
        {
            Assert.Equal(failures.GetInt32(), log.TestFailures.Count);
        }
    }

    [Fact]
    public void Every_scenario_has_a_sample()
    {
        var scenarios = Directory.GetDirectories(Path.Combine(RepoRoot, "eval", "scenarios")).Select(Path.GetFileName);
        var samples = Directory.GetFiles(Path.Combine(RepoRoot, "samples")).Select(Path.GetFileNameWithoutExtension).ToHashSet();

        Assert.All(scenarios, s => Assert.Contains(s, samples));
    }

    private static string RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "DotnetDiag.sln")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Repository root (DotnetDiag.sln) not found.");
        }
    }
}
