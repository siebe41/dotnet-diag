using System.Text.Json;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;
using DotnetDiag.Gui.ViewModels;

namespace DotnetDiag.Gui.Tests;

/// <summary>The Analyze page end to end on real fixture logs: everything the <c>analyze</c> command can do.</summary>
public sealed class AnalyzeViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeDesktop _desktop = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public Task Opening_a_log_shows_what_the_cli_reports() => Headless.Run(async () =>
    {
        var viewModel = Create();
        var path = Fixtures.Path("missing-package-cs0246.txt");

        await viewModel.OpenPathAsync(path);

        var report = viewModel.Report!;
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("BUILD FAILED", report.BadgeText);
        Assert.Equal(Tone.Error, report.BadgeTone);
        Assert.Equal(5, report.ErrorCount);
        Assert.Equal(1, report.WarningCount);
        Assert.Equal(["CS0246", "CS0103", "CS1061", "CS0006"], report.Diagnostics.Select(d => d.Code));
        Assert.Equal("×2", report.Diagnostics[1].Count);
        Assert.Contains("CS8618 ×1", report.DiagnosticsNote);
        Assert.Contains(report.MissingPackages, p => p.Package == "Newtonsoft.Json" && p.Command.EndsWith("package Newtonsoft.Json", StringComparison.Ordinal));
        Assert.Contains(report.Hints, h => h is { Code: "CS0006", IsLikelySymptom: true });
        Assert.False(report.HasAi);
        Assert.NotNull(report.TipText);
        Assert.Equal(report.StatusLine, viewModel.StatusText);
        Assert.Equal(path, viewModel.RecentInputs[0]);

        // The Markdown and JSON tabs are the CLI's own --format outputs.
        Assert.Contains("## Errors", report.Markdown);
        using var json = JsonDocument.Parse(report.Json);
        Assert.Equal(5, json.RootElement.GetProperty("errorCount").GetInt32());
        Assert.Equal("none", json.RootElement.GetProperty("ai").GetProperty("provider").GetString());

        // --show-context: nothing was sent, but the exact bundle is on display.
        Assert.StartsWith("Diagnose this .NET failure.", report.PromptUser);
        Assert.Contains("Nothing was sent", report.ContextNote);
        Assert.InRange(report.ContextTokens, 1, report.ContextBudget);
    });

    [Fact]
    public Task Warnings_toggle_and_filter_narrow_the_diagnostics() => Headless.Run(async () =>
    {
        var viewModel = Create();
        await viewModel.OpenPathAsync(Fixtures.Path("missing-package-cs0246.txt"));
        var report = viewModel.Report!;

        viewModel.ShowWarnings = true; // --warnings
        Assert.Contains(report.Diagnostics, d => d is { Code: "CS8618", IsError: false });
        Assert.Null(report.DiagnosticsNote);

        report.Filter = "cs1061";
        Assert.Equal("CS1061", Assert.Single(report.Diagnostics).Code);

        report.Filter = "no such thing";
        Assert.Empty(report.Diagnostics);
        Assert.Equal("Nothing matches the filter.", report.DiagnosticsNote);

        // The toggle inside the report and the run option are the same switch.
        report.Filter = string.Empty;
        report.ShowWarnings = false;
        Assert.False(viewModel.ShowWarnings);
        Assert.DoesNotContain(report.Diagnostics, d => !d.IsError);
    });

    [Fact]
    public Task Exit_code_gate_reports_what_the_cli_would_return() => Headless.Run(async () =>
    {
        var viewModel = Create();
        await viewModel.OpenPathAsync(Fixtures.Path("missing-package-cs0246.txt"));
        var report = viewModel.Report!;

        Assert.Equal(0, report.ExitCode);
        Assert.Equal("exit code 0 (1 with --exit-code)", report.ExitCodeText);

        viewModel.ExitCodeGate = true;

        Assert.Equal(1, report.ExitCode);
        Assert.Equal("exit code 1", report.ExitCodeText);
        Assert.EndsWith("--exit-code", viewModel.CommandPreview);
    });

    [Fact]
    public Task Pasted_output_is_analyzed_like_piped_input() => Headless.Run(async () =>
    {
        var viewModel = Create();
        _desktop.Clipboard = File.ReadAllText(Fixtures.Path("dotnet-test-vstest.txt"));

        await viewModel.PasteCommand.ExecuteAsync(null);

        var report = viewModel.Report!;
        Assert.Equal(AnalysisService.PastedSourceName, report.SourceName);
        Assert.Equal("dotnet test output", report.KindName);
        Assert.Equal("TESTS FAILED", report.BadgeText);
        Assert.Equal(3, report.Tests.Count);
        Assert.StartsWith("3 failed", report.TestsText);
        Assert.Contains(report.Tests, t => t.HasStackTrace && t.Location.EndsWith(".cs:21", StringComparison.Ordinal));
        Assert.StartsWith("Pasted log", viewModel.PastedSummary);
        Assert.StartsWith("<command> 2>&1 | dotnet-diag analyze", viewModel.CommandPreview);
        Assert.Empty(viewModel.RecentInputs);

        // Typing a path replaces the pasted log.
        viewModel.InputPath = "other.binlog";
        Assert.Null(viewModel.PastedSummary);
        Assert.Equal("other.binlog", viewModel.BuildRequest().InputPath);
        Assert.Null(viewModel.BuildRequest().PastedText);
    });

    [Fact]
    public Task A_copied_path_or_an_empty_clipboard_is_handled() => Headless.Run(async () =>
    {
        var viewModel = Create();

        _desktop.Clipboard = "   ";
        await viewModel.PasteCommand.ExecuteAsync(null);
        Assert.Contains("clipboard has no text", viewModel.ErrorMessage);
        Assert.Null(viewModel.Report);

        _desktop.Clipboard = $"\"{Fixtures.Path("syntax-errors.txt")}\"";
        await viewModel.PasteCommand.ExecuteAsync(null);
        Assert.Equal("syntax-errors.txt", viewModel.Report!.SourceName);
        Assert.Null(viewModel.PastedSummary);
    });

    [Fact]
    public Task A_folder_resolves_to_its_newest_binlog() => Headless.Run(async () =>
    {
        var viewModel = Create();
        var folder = Directory.CreateDirectory(_temp.File("logs")).FullName;

        await viewModel.OpenPathAsync(folder);
        Assert.Contains("No .binlog files found", viewModel.ErrorMessage);

        // The format is detected by content, so text with a .binlog name is enough to prove which file is picked.
        var older = Path.Combine(folder, "older.binlog");
        var newer = Path.Combine(folder, "newer.binlog");
        File.Copy(Fixtures.Path("syntax-errors.txt"), older);
        File.Copy(Fixtures.Path("nuget-restore.txt"), newer);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        await viewModel.OpenPathAsync(folder);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("newer.binlog", viewModel.Report!.SourceName);
    });

    [Theory]
    [InlineData("", "No input")]
    [InlineData("does-not-exist.binlog", "File not found")]
    public Task Unusable_input_is_explained_and_the_page_stays_usable(string input, string expected) => Headless.Run(async () =>
    {
        var viewModel = Create();
        viewModel.InputPath = input;

        await viewModel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Contains(expected, viewModel.ErrorMessage);
        Assert.Null(viewModel.Report);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("Invalid input", viewModel.StatusText);

        await viewModel.OpenPathAsync(Fixtures.Path("syntax-errors.txt"));
        Assert.Null(viewModel.ErrorMessage);
        Assert.NotNull(viewModel.Report);
    });

    [Fact]
    public Task Opening_a_second_log_while_one_is_running_shows_only_the_second() => Headless.Run(async () =>
    {
        var viewModel = Create();

        var first = viewModel.OpenPathAsync(Fixtures.Path("missing-package-cs0246.txt"));
        var second = viewModel.OpenPathAsync(Fixtures.Path("dotnet-test-vstest.txt"));
        await Task.WhenAll(first, second);

        var report = viewModel.Report!;
        Assert.Equal("dotnet-test-vstest.txt", report.SourceName);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(report.StatusLine, viewModel.StatusText); // not "Canceled" from the run that was superseded
    });

    [Fact]
    public void Command_preview_mirrors_every_run_option_except_the_api_key()
    {
        var viewModel = Create();
        Assert.Equal("dotnet-diag analyze --offline", viewModel.CommandPreview);
        Assert.False(viewModel.AiOptionsEnabled);

        viewModel.InputPath = "logs/my build.binlog";
        viewModel.Provider = viewModel.ProviderChoices.Single(c => c.Value == ProviderKind.Anthropic);
        viewModel.Model = "claude-x";
        viewModel.Endpoint = "https://gateway.test/v1";
        viewModel.ApiKey = "sk-secret";
        viewModel.Effort = viewModel.EffortChoices.Single(c => c.Value == "high");
        viewModel.TimeoutSeconds = 60;
        viewModel.Repository = "src/shop";
        viewModel.TokenBudget = 2000;
        viewModel.ShowWarnings = true;

        // Values with spaces are quoted the way the local shell expects.
        var quote = OperatingSystem.IsWindows() ? '"' : '\'';
        Assert.True(viewModel.AiOptionsEnabled);
        Assert.Equal(
            $"dotnet-diag analyze {quote}logs/my build.binlog{quote} --provider anthropic --model claude-x --endpoint https://gateway.test/v1 --effort high --timeout 60 --repo src/shop --max-context-tokens 2000 --warnings",
            viewModel.CommandPreview);
        Assert.DoesNotContain("sk-secret", viewModel.CommandPreview);

        // The key still reaches the analysis itself.
        var request = viewModel.BuildRequest();
        Assert.Equal("sk-secret", request.Overrides.ApiKey);
        Assert.Equal(ProviderKind.Anthropic, request.Overrides.Kind);
        Assert.Equal(2000, request.Context.MaxTokens);
        Assert.False(request.Offline);
    }

    [Theory]
    [InlineData("a$(curl evil).binlog")]
    [InlineData("a`id`.log")]
    [InlineData("it's & a | log;.txt")]
    public void A_hostile_file_name_cannot_run_anything_when_the_copied_command_is_pasted(string name)
    {
        var quoted = CliCommandBuilder.Quote(name);

        // Single quotes are literal in POSIX shells and in PowerShell; $ and backticks never sit inside double quotes.
        if (name.Contains('$') || name.Contains('`') || !OperatingSystem.IsWindows())
        {
            Assert.StartsWith("'", quoted);
            Assert.EndsWith("'", quoted);
        }
        else
        {
            Assert.Equal($"\"{name}\"", quoted);
        }

        Assert.Contains(quoted, CliCommandBuilder.Build(new AnalysisRequest { InputPath = name, Offline = true }));
    }

    [Fact]
    public void Quoting_leaves_plain_values_alone_and_survives_a_trailing_backslash()
    {
        Assert.Equal("msbuild.binlog", CliCommandBuilder.Quote("msbuild.binlog"));
        Assert.Equal("https://gateway.test/v1", CliCommandBuilder.Quote("https://gateway.test/v1"));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(@"C:\logs\build.binlog", CliCommandBuilder.Quote(@"C:\logs\build.binlog"));
            Assert.Equal("\"C:\\my logs\\\\\"", CliCommandBuilder.Quote(@"C:\my logs\")); // the closing quote stays a quote
        }
        else
        {
            Assert.Equal(@"'C:\logs\build.binlog'", CliCommandBuilder.Quote(@"C:\logs\build.binlog"));
            Assert.Equal(@"'it'\''s'", CliCommandBuilder.Quote("it's"));
        }
    }

    [Fact]
    public Task Cancel_returns_at_once_and_shows_no_report() => Headless.Run(async () =>
    {
        var viewModel = Create();
        viewModel.InputPath = Fixtures.Path("missing-package-cs0246.txt");

        var running = viewModel.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        viewModel.AnalyzeCancelCommand.Execute(null);
        await running;

        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.Report);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Canceled", viewModel.StatusText);
    });

    [Fact]
    public Task Only_files_inside_the_workspace_are_opened_from_a_log() => Headless.Run(async () =>
    {
        // A repository with one source file, and a file of the same kind outside it that a crafted log points at.
        var repo = Directory.CreateDirectory(_temp.File("repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(Path.Combine(repo, "src"));
        var inside = Path.Combine(repo, "src", "App.cs");
        var outside = _temp.File("Outside.cs");
        File.WriteAllText(inside, "class App {}");
        File.WriteAllText(outside, "class Outside {}");

        var viewModel = Create();
        viewModel.Repository = repo;
        await viewModel.OpenTextAsync(
            $"""
            {inside}(1,7): error CS0246: The type or namespace name 'Foo' could not be found
            {outside}(1,1): error CS1002: ; expected
            Build FAILED.
            """);
        var report = viewModel.Report!;
        var insideRow = report.Diagnostics.Single(d => d.Code == "CS0246");
        var outsideRow = report.Diagnostics.Single(d => d.Code == "CS1002");

        await report.OpenLocationCommand.ExecuteAsync(insideRow);
        Assert.Equal([inside], _desktop.OpenedFiles);
        Assert.Null(_desktop.Clipboard);

        await report.OpenLocationCommand.ExecuteAsync(outsideRow);
        Assert.Equal([inside], _desktop.OpenedFiles); // still only the first: the outside file was not opened
        Assert.Equal(outsideRow.Location, _desktop.Clipboard);
    });

    [Fact]
    public void Changing_a_default_in_settings_applies_to_the_page_immediately()
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        var settings = new SettingsViewModel(store, _desktop, _temp.File("config.json"));
        var viewModel = new AnalyzeViewModel(settings, _desktop);
        Assert.Null(viewModel.Provider.Value);

        settings.AlwaysOffline = true; // "Start offline" must not wait for a restart
        Assert.Equal(ProviderKind.None, viewModel.Provider.Value);
        Assert.True(viewModel.BuildRequest().Offline);

        settings.AlwaysOffline = false;
        Assert.Null(viewModel.Provider.Value);

        settings.TokenBudget = 3000;
        settings.ShowWarnings = true;
        settings.ExitCodeGate = true;
        Assert.Equal(3000, viewModel.TokenBudget);
        Assert.True(viewModel.ShowWarnings);
        Assert.True(viewModel.ExitCodeGate);

        // A provider picked for this run is not undone by switching the default back off.
        viewModel.Provider = viewModel.ProviderChoices.Single(c => c.Value == ProviderKind.Ollama);
        settings.AlwaysOffline = true;
        settings.AlwaysOffline = false;
        Assert.Null(viewModel.Provider.Value);
    }

    [Fact]
    public void Provider_line_follows_the_resolved_configuration()
    {
        var viewModel = Create();
        Assert.Equal("AI off · local hints only", viewModel.ProviderSummary);

        viewModel.Provider = viewModel.ProviderChoices.Single(c => c.Value == ProviderKind.Anthropic);
        Assert.Equal("Anthropic is not usable yet", viewModel.ProviderSummary);
        Assert.Equal(Tone.Warning, viewModel.ProviderTone);

        viewModel.ApiKey = "sk-test";
        Assert.Equal($"Anthropic · {ProviderConfiguration.DefaultAnthropicModel}", viewModel.ProviderSummary);
        Assert.Equal(Tone.Violet, viewModel.ProviderTone);
    }

    [Fact]
    public Task An_unusable_provider_falls_back_to_local_hints_with_a_warning() => Headless.Run(async () =>
    {
        var viewModel = Create();
        viewModel.Provider = viewModel.ProviderChoices.Single(c => c.Value == ProviderKind.OpenAI); // no model: cannot be called

        await viewModel.OpenPathAsync(Fixtures.Path("missing-package-cs0246.txt"));

        Assert.Contains("needs a model", viewModel.ProviderWarning);
        Assert.EndsWith("Continuing with local hints only.", viewModel.ProviderWarning);
        var report = viewModel.Report!;
        Assert.False(report.HasAi);
        Assert.True(report.HasHints);
        Assert.Null(report.TipText); // the warning already says why there is no AI answer
    });

    [Fact]
    public Task Report_actions_copy_save_explain_and_close() => Headless.Run(async () =>
    {
        var viewModel = Create();
        string? explained = null;
        viewModel.ExplainRequested += code => explained = code;
        await viewModel.OpenPathAsync(Fixtures.Path("missing-package-cs0246.txt"));
        var report = viewModel.Report!;

        report.ExplainCommand.Execute("CS0246");
        Assert.Equal("CS0246", explained);

        // The file is from a CI machine and not on this one: the location is copied instead of opened.
        await report.OpenLocationCommand.ExecuteAsync(report.Diagnostics[0]);
        Assert.Equal(report.Diagnostics[0].Location, _desktop.Clipboard);
        Assert.Empty(_desktop.OpenedFiles);

        _desktop.SavePath = _temp.File("report.json");
        await report.SaveJsonCommand.ExecuteAsync(null);
        Assert.Equal(report.Json, File.ReadAllText(_desktop.SavePath));
        Assert.StartsWith("Saved", report.SaveStatus);

        _desktop.SavePath = _temp.File("report.md");
        await report.SaveMarkdownCommand.ExecuteAsync(null);
        Assert.Equal(report.Markdown, File.ReadAllText(_desktop.SavePath));

        await viewModel.CopyCommandCommand.ExecuteAsync(null);
        Assert.Equal(viewModel.CommandPreview, _desktop.Clipboard);

        viewModel.CloseReportCommand.Execute(null);
        Assert.Null(viewModel.Report);
        Assert.False(viewModel.HasReport);
        Assert.Equal("Ready", viewModel.StatusText);
    });

    [Fact]
    public void Settings_provide_the_starting_options()
    {
        var viewModel = Create(new GuiSettings { TokenBudget = 4000, ShowWarnings = true, ExitCodeGate = true, MaxSnippets = 1, DefaultRepository = _temp.Root });

        Assert.Equal(4000, viewModel.TokenBudget);
        Assert.True(viewModel.ShowWarnings);
        Assert.True(viewModel.ExitCodeGate);
        Assert.Null(viewModel.Provider.Value); // not forced offline: the configuration decides

        var request = viewModel.BuildRequest();
        Assert.Equal(4000, request.Context.MaxTokens);
        Assert.Equal(1, request.Context.MaxSnippets);
        Assert.Equal(_temp.Root, request.DefaultRepository);
        Assert.Equal(_temp.Root, AnalysisService.ResolveWorkspace(request, inputPath: null)!.Root);
    }

    /// <summary>Starts offline unless told otherwise, so no test can reach a real provider.</summary>
    private AnalyzeViewModel Create(GuiSettings? settings = null)
    {
        var store = new GuiSettingsStore(_temp.File("gui.json"));
        Assert.True(store.TrySave(settings ?? new GuiSettings { AlwaysOffline = true }));
        return new AnalyzeViewModel(new SettingsViewModel(store, _desktop, _temp.File("config.json")), _desktop);
    }
}
