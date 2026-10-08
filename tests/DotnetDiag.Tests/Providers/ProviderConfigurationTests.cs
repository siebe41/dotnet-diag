using DotnetDiag.Core.Providers;

namespace DotnetDiag.Tests.Providers;

public sealed class ProviderConfigurationTests : IDisposable
{
    private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"dotnet-diag-config-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_configPath);

    [Fact]
    public void Api_keys_alone_never_enable_a_provider()
    {
        var settings = Resolve(env: new() { ["ANTHROPIC_API_KEY"] = "sk", ["OPENAI_API_KEY"] = "sk" });

        Assert.Equal(ProviderKind.None, settings.Kind);
        Assert.False(settings.IsEnabled);
        Assert.Null(settings.ApiKey);
        Assert.Equal("default", settings.Origins["Provider"]);
    }

    [Fact]
    public void Anthropic_from_environment_uses_defaults()
    {
        var settings = Resolve(env: new() { ["DOTNET_DIAG_PROVIDER"] = "anthropic", ["ANTHROPIC_API_KEY"] = "sk-ant" });

        Assert.True(settings.IsEnabled);
        Assert.Equal("claude-opus-5-5", settings.Model);
        Assert.Equal(new Uri("https://api.anthropic.com"), settings.Endpoint);
        Assert.Equal("sk-ant", settings.ApiKey);
        Assert.Equal("DOTNET_DIAG_PROVIDER", settings.Origins["Provider"]);
        Assert.Equal("ANTHROPIC_API_KEY", settings.Origins["ApiKey"]);
        Assert.Equal("default", settings.Origins["Model"]);
    }

    [Fact]
    public void Anthropic_without_a_key_reports_a_problem()
    {
        var settings = Resolve(new ProviderOverrides(ProviderKind.Anthropic));

        Assert.False(settings.IsEnabled);
        Assert.Contains("ANTHROPIC_API_KEY", settings.Problem);
    }

    [Fact]
    public void OpenAI_requires_a_model_but_not_a_key_for_self_hosted_endpoints()
    {
        Assert.Contains("--model", Resolve(new ProviderOverrides(ProviderKind.OpenAI, ApiKey: "k")).Problem);
        Assert.Contains("OPENAI_API_KEY", Resolve(new ProviderOverrides(ProviderKind.OpenAI, Model: "gpt-x")).Problem);

        var local = Resolve(new ProviderOverrides(ProviderKind.OpenAI, Model: "qwen", Endpoint: "http://localhost:1234/v1"));
        Assert.True(local.IsEnabled);
        Assert.Equal(new Uri("http://localhost:1234/v1"), local.Endpoint);
    }

    [Fact]
    public void Command_line_overrides_environment_which_overrides_config_file()
    {
        File.WriteAllText(_configPath, """
            {
              // comments are allowed
              "provider": "ollama",
              "model": "from-file",
              "endpoint": "http://gpu-box:11434",
              "timeoutSeconds": 30,
            }
            """);

        var fromFile = Resolve();
        Assert.Equal(ProviderKind.Ollama, fromFile.Kind);
        Assert.Equal("from-file", fromFile.Model);
        Assert.Equal(new Uri("http://gpu-box:11434"), fromFile.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(30), fromFile.Timeout);
        Assert.True(fromFile.ConfigFileFound);

        var fromEnv = Resolve(env: new() { ["DOTNET_DIAG_MODEL"] = "from-env" });
        Assert.Equal("from-env", fromEnv.Model);
        Assert.Equal("DOTNET_DIAG_MODEL", fromEnv.Origins["Model"]);

        var fromFlag = Resolve(new ProviderOverrides(Model: "from-flag"), new() { ["DOTNET_DIAG_MODEL"] = "from-env" });
        Assert.Equal("from-flag", fromFlag.Model);
        Assert.Equal("command line", fromFlag.Origins["Model"]);

        var offline = Resolve(new ProviderOverrides(ProviderKind.None));
        Assert.Equal(ProviderKind.None, offline.Kind);
    }

    [Theory]
    [InlineData("0.0.0.0:11434", "http://localhost:11434/")]
    [InlineData("gpu-box:8080", "http://gpu-box:8080/")]
    [InlineData("https://ollama.internal/", "https://ollama.internal/")]
    public void Normalizes_ollama_host_values(string value, string expected)
    {
        var settings = Resolve(new ProviderOverrides(ProviderKind.Ollama), new() { ["OLLAMA_HOST"] = value });

        Assert.Equal(expected, settings.Endpoint!.ToString());
        Assert.Equal("llama3.1", settings.Model);
    }

    [Fact]
    public void Invalid_endpoint_is_reported()
    {
        var settings = Resolve(new ProviderOverrides(ProviderKind.Ollama, Endpoint: "ftp://nope"));

        Assert.Contains("not a valid endpoint", settings.Problem);
    }

    [Fact]
    public void Config_file_saves_only_what_is_set_and_resolves_the_same_way_afterwards()
    {
        Assert.Null(UserConfigFile.Load(_configPath));

        new UserConfigFile { Provider = "ollama", Model = "qwen2.5-coder:14b", TimeoutSeconds = 45 }.Save(_configPath);

        var text = File.ReadAllText(_configPath);
        Assert.Contains("\"provider\": \"ollama\"", text);
        Assert.Contains("\"timeoutSeconds\": 45", text);
        Assert.DoesNotContain("apiKey", text);
        Assert.DoesNotContain("null", text);

        var settings = Resolve();
        Assert.Equal(ProviderKind.Ollama, settings.Kind);
        Assert.Equal("qwen2.5-coder:14b", settings.Model);
        Assert.Equal(TimeSpan.FromSeconds(45), settings.Timeout);
        Assert.Equal("config file", settings.Origins["Timeout"]);
    }

    [Fact]
    public void Config_file_keeps_keys_it_does_not_know_when_rewritten()
    {
        File.WriteAllText(_configPath, """
            {
              // written by a newer version
              "Provider": "anthropic",
              "apiKey": "sk-from-file",
              "futureSetting": { "enabled": true },
            }
            """);

        var loaded = UserConfigFile.Load(_configPath)!;
        Assert.Equal("anthropic", loaded.Provider);
        (loaded with { Model = "claude-x", ApiKey = null }).Save(_configPath);

        var reloaded = UserConfigFile.Load(_configPath)!;
        Assert.Equal("claude-x", reloaded.Model);
        Assert.Null(reloaded.ApiKey);
        Assert.True(reloaded.Extra!["futureSetting"].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Unreadable_config_file_is_ignored()
    {
        File.WriteAllText(_configPath, "{ this is not json");

        Assert.Null(UserConfigFile.Load(_configPath));
        Assert.Equal(ProviderKind.None, Resolve().Kind);
        Assert.False(Resolve().ConfigFileFound);
    }

    private ProviderSettings Resolve(ProviderOverrides? overrides = null, Dictionary<string, string>? env = null) =>
        ProviderConfiguration.Resolve(overrides, name => env?.GetValueOrDefault(name), _configPath);
}
