using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;
using DotnetDiag.Gui.ViewModels;

namespace DotnetDiag.Gui.Tests;

/// <summary>The counterparts of <c>dotnet-diag explain</c> and <c>dotnet-diag doctor</c>.</summary>
public sealed class ExplainAndDoctorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeDesktop _desktop = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Explain_lists_the_whole_catalog_in_code_order()
    {
        var viewModel = new ExplainViewModel();

        Assert.Equal(KnownErrorCatalog.All.Count, viewModel.Entries.Count);
        Assert.Equal(viewModel.Entries.Select(e => e.Code).Order(StringComparer.Ordinal), viewModel.Entries.Select(e => e.Code));
        Assert.Equal($"{KnownErrorCatalog.All.Count} codes", viewModel.CountText);
        Assert.Same(viewModel.Entries[0], viewModel.Selected);
        Assert.Null(viewModel.UnknownCode);
    }

    [Fact]
    public void Explain_finds_a_code_whatever_its_casing()
    {
        var viewModel = new ExplainViewModel { Query = " nu1605 " };

        var selected = viewModel.Selected!;
        Assert.Equal("NU1605", selected.Code);
        Assert.Equal("Package downgrade detected", selected.Title);
        Assert.Equal("NuGet", selected.CategoryName);
        Assert.Equal(Tone.Violet, selected.Tone);
        Assert.NotEmpty(selected.Fixes);
        Assert.EndsWith("/nu1605", selected.DocsUrl);
        Assert.Contains(selected.Fixes[0], selected.CopyText);
        Assert.Null(viewModel.UnknownCode);
    }

    [Fact]
    public void Explain_searches_words_and_filters_by_category()
    {
        var viewModel = new ExplainViewModel { Query = "downgrade" };
        Assert.Contains(viewModel.Entries, e => e.Code == "NU1605");
        Assert.True(viewModel.Entries.Count < KnownErrorCatalog.All.Count);

        viewModel.Query = string.Empty;
        viewModel.Category = viewModel.Categories.Single(c => c.Value == DiagnosticCategory.MSBuild);
        Assert.NotEmpty(viewModel.Entries);
        Assert.All(viewModel.Entries, e => Assert.StartsWith("MSB", e.Code));
        Assert.Contains(" of ", viewModel.CountText);

        // Jumping to a code from a report clears the category filter.
        viewModel.Show("CS0246");
        Assert.Null(viewModel.Category.Value);
        Assert.Equal("CS0246", viewModel.Selected!.Code);
    }

    [Fact]
    public void Explain_points_at_the_docs_for_a_code_it_does_not_know()
    {
        var viewModel = new ExplainViewModel { Query = "cs9999" };

        Assert.Equal("CS9999", viewModel.UnknownCode);
        Assert.Equal("CS9999 is not in the offline catalog.", viewModel.UnknownText);
        Assert.Equal(KnownErrorCatalog.DocsUrlFor("cs9999"), viewModel.UnknownDocsUrl);
        Assert.Null(viewModel.Selected);
        Assert.Empty(viewModel.Entries);

        viewModel.Query = "some words";
        Assert.Null(viewModel.UnknownCode);
    }

    [Fact]
    public void Doctor_shows_each_value_with_its_source_and_never_a_secret()
    {
        var environment = new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "sk-secret-value", ["DOTNET_DIAG_TIMEOUT"] = "30" };
        var doctor = CreateDoctor(new AnalysisRequest { Overrides = new ProviderOverrides(ProviderKind.Anthropic, Model: "claude-x") }, environment);

        Assert.Equal(new DoctorRow("Provider", "Anthropic", "run options"), doctor.Rows[0]);
        Assert.Contains(new DoctorRow("Model", "claude-x", "run options"), doctor.Rows);
        Assert.Contains(new DoctorRow("Endpoint", "https://api.anthropic.com/", "default"), doctor.Rows);
        Assert.Contains(new DoctorRow("API key", "set", "ANTHROPIC_API_KEY", Tone.Success), doctor.Rows);
        Assert.Contains(new DoctorRow("Timeout", "30s", "DOTNET_DIAG_TIMEOUT"), doctor.Rows);
        Assert.Contains(doctor.Rows, r => r is { Setting: "Config file", Source: "not found" });
        Assert.Contains(doctor.Rows, r => r.Setting == "Git workspace");
        Assert.Equal("Provider configuration looks usable.", doctor.Status);
        Assert.Equal(Tone.Success, doctor.StatusTone);

        Assert.Equal(new VariableRow("ANTHROPIC_API_KEY", "Key used when the provider is Anthropic", "set (hidden)", true),
            doctor.Variables.Single(v => v.Name == "ANTHROPIC_API_KEY"));
        Assert.Equal("30", doctor.Variables.Single(v => v.Name == "DOTNET_DIAG_TIMEOUT").Value);
        Assert.False(doctor.Variables.Single(v => v.Name == "OLLAMA_HOST").IsSet);

        doctor.CopyReportCommand.Execute(null);
        Assert.Contains("Provider", _desktop.Clipboard);
        Assert.DoesNotContain("sk-secret-value", _desktop.Clipboard);
        Assert.DoesNotContain(doctor.Rows, r => r.Value.Contains("sk-secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public void Doctor_explains_a_missing_key_and_the_offline_default()
    {
        var missingKey = CreateDoctor(new AnalysisRequest { Overrides = new ProviderOverrides(ProviderKind.Anthropic) }, []);
        Assert.Contains("ANTHROPIC_API_KEY", missingKey.Status);
        Assert.Equal(Tone.Warning, missingKey.StatusTone);
        Assert.Contains(new DoctorRow("API key", "not set", "-", Tone.Neutral), missingKey.Rows);

        var none = CreateDoctor(new AnalysisRequest(), new Dictionary<string, string> { ["OPENAI_API_KEY"] = "sk" });
        Assert.StartsWith("No AI provider selected", none.Status);
        Assert.Equal(["Provider", "Config file", "Git workspace"], none.Rows.Select(r => r.Setting));
        Assert.Equal(new DoctorRow("Provider", "none", "default"), none.Rows[0]);

        var offline = CreateDoctor(new AnalysisRequest { Offline = true, Overrides = new ProviderOverrides(ProviderKind.Ollama) }, []);
        Assert.Equal(new DoctorRow("Provider", "none", "run options"), offline.Rows[0]);
    }

    [Fact]
    public void Doctor_hides_credentials_embedded_in_an_endpoint()
    {
        var environment = new Dictionary<string, string> { ["OLLAMA_HOST"] = "https://alice:hunter2@ollama.internal:11434" };
        var doctor = CreateDoctor(new AnalysisRequest { Overrides = new ProviderOverrides(ProviderKind.Ollama) }, environment);

        Assert.Equal("https://***@ollama.internal:11434/", doctor.Rows.Single(r => r.Setting == "Endpoint").Value);
        Assert.Equal("https://***@ollama.internal:11434", doctor.Variables.Single(v => v.Name == "OLLAMA_HOST").Value);

        doctor.CopyReportCommand.Execute(null);
        Assert.DoesNotContain("hunter2", _desktop.Clipboard);
    }

    [Fact]
    public void Doctor_reports_the_workspace_a_folder_input_would_use()
    {
        // The repository is found from the newest binlog inside the folder, exactly as the analysis does.
        var repo = Directory.CreateDirectory(_temp.File("repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var logs = Directory.CreateDirectory(Path.Combine(repo, "artifacts")).FullName;
        File.WriteAllText(Path.Combine(logs, "msbuild.binlog"), "Build succeeded.");

        var doctor = CreateDoctor(new AnalysisRequest { InputPath = logs }, []);

        Assert.Equal(repo, doctor.Rows.Single(r => r.Setting == "Git workspace").Value);
    }

    [Fact]
    public void Doctor_reads_the_config_file_and_opens_its_folder()
    {
        var configPath = _temp.File("config.json");
        new UserConfigFile { Provider = "ollama", Model = "from-file" }.Save(configPath);
        var doctor = CreateDoctor(new AnalysisRequest(), [], configPath);

        Assert.Contains(new DoctorRow("Model", "from-file", "config file"), doctor.Rows);
        Assert.Contains(new DoctorRow("Config file", configPath, "found", Tone.Success), doctor.Rows);

        doctor.OpenConfigFolderCommand.Execute(null);
        Assert.Equal([_temp.Root], _desktop.OpenedFolders);

        var requested = false;
        doctor.SettingsRequested += () => requested = true;
        doctor.EditSettingsCommand.Execute(null);
        Assert.True(requested);
    }

    [Fact]
    public Task Shell_wires_report_links_and_saved_configuration_between_pages() => Headless.Run(async () =>
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        store.TrySave(new GuiSettings { AlwaysOffline = true });
        var shell = new MainWindowViewModel(store, _desktop);
        Assert.Same(shell.Analyze, shell.CurrentPage);

        await shell.Analyze.OpenPathAsync(Fixtures.Path("nuget-restore.txt"));
        var code = shell.Analyze.Report!.Hints[0].Code;
        shell.Analyze.Report.ExplainCommand.Execute(code);

        Assert.Equal(MainWindowViewModel.ExplainPage, shell.SelectedPageIndex);
        Assert.Same(shell.Explain, shell.CurrentPage);
        Assert.Equal(code, shell.Explain.Query);

        shell.NavigateCommand.Execute("2");
        Assert.Same(shell.Doctor, shell.CurrentPage);
        shell.Doctor.EditSettingsCommand.Execute(null);
        Assert.Same(shell.Settings, shell.CurrentPage);

        shell.NavigateCommand.Execute("9"); // not a page: ignored
        Assert.Same(shell.Settings, shell.CurrentPage);
    });

    private DoctorViewModel CreateDoctor(AnalysisRequest request, Dictionary<string, string> environment, string? configPath = null)
    {
        configPath ??= _temp.File("absent.json");
        return new DoctorViewModel(
            () => request,
            _desktop,
            r => ProviderConfiguration.Resolve(
                r.Offline ? r.Overrides with { Kind = ProviderKind.None } : r.Overrides,
                name => environment.GetValueOrDefault(name),
                configPath),
            name => environment.GetValueOrDefault(name));
    }
}
