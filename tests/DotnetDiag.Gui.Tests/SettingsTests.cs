using System.Text.Json;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;
using DotnetDiag.Gui.ViewModels;

namespace DotnetDiag.Gui.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Missing_or_damaged_settings_fall_back_to_defaults()
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        Assert.Equal(ThemeManager.SystemThemeId, store.Load().Theme);

        File.WriteAllText(store.Path, "{ not json");
        var settings = store.Load();

        Assert.Equal(1000, settings.TokenBudget);
        Assert.True(settings.AutoAnalyze);
        Assert.Empty(settings.RecentInputs);
    }

    [Fact]
    public void Settings_round_trip_and_out_of_range_values_are_clamped()
    {
        var store = new GuiSettingsStore(_temp.File(Path.Combine("nested", "gui.json")));
        Assert.True(store.TrySave(new GuiSettings { Theme = "nord", TextScale = 1.25, CompactDensity = true, RecentInputs = ["a.binlog"] }));

        var loaded = store.Load();
        Assert.Equal("nord", loaded.Theme);
        Assert.Equal(1.25, loaded.TextScale);
        Assert.True(loaded.CompactDensity);
        Assert.Equal(["a.binlog"], loaded.RecentInputs);

        File.WriteAllText(store.Path, """{ "textScale": 9, "tokenBudget": 5, "maxSnippets": -3, "theme": "", "windowWidth": 10 }""");
        var clamped = store.Load();
        Assert.Equal(ThemeManager.MaxTextScale, clamped.TextScale);
        Assert.Equal(200, clamped.TokenBudget);
        Assert.Equal(0, clamped.MaxSnippets);
        Assert.Equal(ThemeManager.SystemThemeId, clamped.Theme);
        Assert.Equal(720, clamped.WindowWidth);
    }

    [Fact]
    public void Recent_inputs_are_deduplicated_capped_and_optional()
    {
        var settings = new GuiSettings();
        for (var i = 0; i < 15; i++)
        {
            settings = settings.WithRecent($"log{i}.binlog");
        }

        settings = settings.WithRecent("log12.binlog");

        Assert.Equal(GuiSettings.MaxRecentInputs, settings.RecentInputs.Count);
        Assert.Equal("log12.binlog", settings.RecentInputs[0]);
        Assert.Single(settings.RecentInputs, p => p == "log12.binlog");

        var forgetful = new GuiSettings { RememberRecent = false }.WithRecent("x.binlog");
        Assert.Empty(forgetful.RecentInputs);
    }

    [Fact]
    public void Changing_a_preference_saves_it_and_announces_appearance_changes()
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        var viewModel = new SettingsViewModel(store, new FakeDesktop(), _temp.File("config.json"));
        var appearanceChanges = 0;
        viewModel.AppearanceChanged += () => appearanceChanges++;

        viewModel.SelectedTheme = viewModel.Themes.Single(t => t.Id == "circuit");
        viewModel.TextScale = 1.2;
        viewModel.TokenBudget = 2500;
        viewModel.AlwaysOffline = true;

        Assert.Equal(2, appearanceChanges); // theme and text size; the other two are not appearance
        var saved = store.Load();
        Assert.Equal("circuit", saved.Theme);
        Assert.Equal(1.2, saved.TextScale);
        Assert.Equal(2500, saved.TokenBudget);
        Assert.True(saved.AlwaysOffline);
        Assert.Equal(2500, viewModel.Current.ToContextOptions(viewModel.Current.TokenBudget).MaxTokens);
    }

    [Fact]
    public void Theme_picker_lists_system_first_then_every_catalog_theme()
    {
        var viewModel = new SettingsViewModel(new GuiSettingsStore(_temp.File("gui.json")), new FakeDesktop(), _temp.File("config.json"));

        Assert.Equal(ThemeManager.SystemThemeId, viewModel.Themes[0].Id);
        Assert.Equal(ThemeCatalog.All.Select(t => t.Id), viewModel.Themes.Skip(1).Select(t => t.Id));
        Assert.Same(viewModel.Themes[0], viewModel.SelectedTheme);
    }

    [Fact]
    public void Reset_restores_defaults_but_keeps_history()
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        store.TrySave(new GuiSettings { Theme = "terminal", TextScale = 1.4, ShowWarnings = true, RecentInputs = ["kept.binlog"] });
        var viewModel = new SettingsViewModel(store, new FakeDesktop(), _temp.File("config.json"));
        Assert.Equal("terminal", viewModel.SelectedTheme.Id);

        viewModel.ResetPreferencesCommand.Execute(null);

        Assert.Equal(ThemeManager.SystemThemeId, viewModel.SelectedTheme.Id);
        Assert.Equal(1, viewModel.TextScale);
        Assert.False(viewModel.ShowWarnings);
        Assert.Equal(["kept.binlog"], store.Load().RecentInputs);

        viewModel.ClearRecentCommand.Execute(null);
        Assert.Empty(viewModel.RecentInputs);
        Assert.Empty(store.Load().RecentInputs);
    }

    [Fact]
    public void Provider_configuration_is_written_to_the_file_the_cli_reads()
    {
        var configPath = _temp.File("config.json");
        File.WriteAllText(configPath, """{ "provider": "claude", "model": "old-model", "futureSetting": { "keep": true } }""");
        var viewModel = new SettingsViewModel(new GuiSettingsStore(_temp.File("gui.json")), new FakeDesktop(), configPath);
        var saved = 0;
        viewModel.ProviderConfigSaved += () => saved++;

        Assert.Equal("anthropic", viewModel.ConfigProvider.Value); // the alias is shown by its canonical name
        Assert.Equal("old-model", viewModel.ConfigModel);
        Assert.False(viewModel.IsProviderDirty);

        viewModel.ConfigProvider = viewModel.ProviderChoices.Single(c => c.Value == "ollama");
        viewModel.ConfigModel = "  qwen2.5-coder:14b ";
        viewModel.ConfigEndpoint = "gpu-box:11434";
        viewModel.ConfigTimeout = 90;
        Assert.True(viewModel.IsProviderDirty);
        viewModel.SaveProviderConfigCommand.Execute(null);

        Assert.Equal(1, saved);
        Assert.False(viewModel.IsProviderDirty);

        // The CLI's own resolver now sees exactly these values.
        var resolved = ProviderConfiguration.Resolve(environment: _ => null, configPath: configPath);
        Assert.Equal(ProviderKind.Ollama, resolved.Kind);
        Assert.Equal("qwen2.5-coder:14b", resolved.Model);
        Assert.Equal(new Uri("http://gpu-box:11434"), resolved.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(90), resolved.Timeout);
        Assert.Equal("config file", resolved.Origins["Model"]);

        // Keys this version does not know survive a save.
        using var json = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.True(json.RootElement.GetProperty("futureSetting").GetProperty("keep").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("apiKey", out _));
    }

    [Fact]
    public void Saving_changes_only_the_fields_that_were_edited()
    {
        var configPath = _temp.File("config.json");
        File.WriteAllText(configPath, """{ "provider": "claude", "effort": "extreme", "timeoutSeconds": 2, "apiKey": "sk-keep" }""");
        var viewModel = new SettingsViewModel(new GuiSettingsStore(_temp.File("gui.json")), new FakeDesktop(), configPath);

        // Meanwhile the CLI user edits the file by hand.
        File.WriteAllText(configPath, """{ "provider": "claude", "effort": "extreme", "timeoutSeconds": 2, "apiKey": "sk-keep", "endpoint": "https://added-later.test" }""");

        viewModel.ConfigModel = "claude-x";
        viewModel.SaveProviderConfigCommand.Execute(null);

        var saved = UserConfigFile.Load(configPath)!;
        Assert.Equal("claude-x", saved.Model);
        Assert.Equal("claude", saved.Provider);                       // an alias is not rewritten
        Assert.Equal("extreme", saved.Effort);                        // a value the page cannot show is not dropped
        Assert.Equal(2, saved.TimeoutSeconds);                        // nor is a number outside the usual range
        Assert.Equal("sk-keep", saved.ApiKey);
        Assert.Equal("https://added-later.test", saved.Endpoint);     // an edit made outside the app survives
        Assert.Equal("https://added-later.test", viewModel.ConfigEndpoint);
        Assert.False(viewModel.IsProviderDirty);
    }

    [Fact]
    public void A_config_file_that_cannot_be_read_is_never_overwritten()
    {
        var configPath = _temp.File("config.json");
        const string Broken = """{ "provider": "ollama", "apiKey": "sk-precious", """;
        File.WriteAllText(configPath, Broken);
        var viewModel = new SettingsViewModel(new GuiSettingsStore(_temp.File("gui.json")), new FakeDesktop(), configPath);

        Assert.Equal(Tone.Warning, viewModel.ProviderStatusTone);
        Assert.Contains("not valid JSON", viewModel.ProviderStatus);

        viewModel.ConfigModel = "anything";
        viewModel.SaveProviderConfigCommand.Execute(null);

        Assert.Equal(Broken, File.ReadAllText(configPath));
        Assert.Equal(Tone.Error, viewModel.ProviderStatusTone);
        Assert.Contains("Nothing was saved", viewModel.ProviderStatus);
        Assert.True(viewModel.IsProviderDirty);
    }

    [Fact]
    public void Unreadable_preferences_are_backed_up_before_they_are_replaced()
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        File.WriteAllText(store.Path, """{ "theme": "nord", oops """);

        Assert.Equal(ThemeManager.SystemThemeId, store.Load().Theme);
        Assert.Equal("""{ "theme": "nord", oops """, File.ReadAllText(store.Path + ".bak"));
    }

    [Fact]
    public void An_invalid_endpoint_is_rejected_and_revert_reloads_the_file()
    {
        var configPath = _temp.File("config.json");
        var viewModel = new SettingsViewModel(new GuiSettingsStore(_temp.File("gui.json")), new FakeDesktop(), configPath);

        viewModel.ConfigEndpoint = "ftp://nope";
        viewModel.SaveProviderConfigCommand.Execute(null);

        Assert.False(File.Exists(configPath));
        Assert.True(viewModel.IsProviderDirty);
        Assert.Equal(Tone.Error, viewModel.ProviderStatusTone);
        Assert.Contains("not a valid", viewModel.ProviderStatus);

        viewModel.RevertProviderConfigCommand.Execute(null);
        Assert.Equal(string.Empty, viewModel.ConfigEndpoint);
        Assert.False(viewModel.IsProviderDirty);
    }
}
