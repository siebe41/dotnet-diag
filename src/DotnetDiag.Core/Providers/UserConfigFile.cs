using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetDiag.Core.Providers;

/// <summary>
/// The user config file (<c>config.json</c>) shared by every dotnet-diag front end. Every value is optional;
/// see <see cref="ProviderConfiguration"/> for how it ranks against flags and environment variables.
/// </summary>
public sealed record UserConfigFile
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public int? TimeoutSeconds { get; init; }

    public int? MaxOutputTokens { get; init; }

    public string? Effort { get; init; }

    /// <summary>Keys this version does not know about, kept so that saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>Reads the file, or returns null when it is missing or unreadable.</summary>
    public static UserConfigFile? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<UserConfigFile>(File.ReadAllText(path), ReadOptions) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the file (creating its folder), omitting unset values. Comments in an existing file are not kept.</summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // Written beside the target and renamed over it, so a crash or a full disk never leaves a half-written
        // file for the CLI to read. The file can hold an API key: where the OS has file modes it is created
        // private to the user, rather than restricted after the key is already on disk.
        var temp = full + ".tmp";
        File.Delete(temp);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(temp, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), options))
        {
            writer.Write(JsonSerializer.Serialize(this, WriteOptions));
            writer.Write(Environment.NewLine);
        }

        File.Move(temp, full, overwrite: true);
    }
}
