using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetDiag.Eval;

/// <summary>An intentionally broken project plus what a useful diagnosis must contain (scenario.json).</summary>
internal sealed record Scenario
{
    public string Name { get; init; } = "";

    public string Directory { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>"build" or "test".</summary>
    public string Command { get; init; } = "build";

    /// <summary>Project to build or test, relative to the scenario folder.</summary>
    public string Target { get; init; } = "";

    /// <summary>Which inputs to analyze: "binlog" and/or "text" (console output).</summary>
    public string[] Inputs { get; init; } = ["binlog", "text"];

    public Expectations Expect { get; init; } = new();

    /// <summary>"auto": apply the tool's own `dotnet add` suggestions and rebuild. "none": no fix to verify.</summary>
    public string Fix { get; init; } = "none";

    /// <summary>When set, the scenario documents a known weakness and does not fail the run.</summary>
    public string? KnownGap { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Scenario Load(string directory)
    {
        var scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(Path.Combine(directory, "scenario.json")), JsonOptions)
            ?? throw new InvalidOperationException($"Empty scenario.json in {directory}");
        return scenario with { Name = Path.GetFileName(directory), Directory = directory };
    }
}

internal sealed record Expectations
{
    /// <summary>Diagnostic codes that must be detected.</summary>
    public string[] Codes { get; init; } = [];

    /// <summary>The first (highest-priority, non-symptom) hint must have this code.</summary>
    public string? TopHint { get; init; }

    /// <summary>Hint codes that must all be present (e.g. exception types for test failures).</summary>
    public string[] HintCodes { get; init; } = [];

    /// <summary>Exact set of suggested packages; an empty array means "must not invent any".</summary>
    public string[]? MissingPackages { get; init; }

    public string[] VersionConflicts { get; init; } = [];

    public int? TestFailures { get; init; }

    /// <summary>Each string must appear in some suggestion, fix command or hint summary.</summary>
    public string[] SuggestionContains { get; init; } = [];

    /// <summary>Checked only when an AI provider is configured: each keyword must appear in the answer.</summary>
    public string[] AiKeywords { get; init; } = [];

    [JsonIgnore]
    public bool HasDetectionChecks => Codes.Length > 0 || TestFailures is not null || HintCodes.Length > 0;
}
