using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using DotnetDiag.Core;
using DotnetDiag.Gui.Controls;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;
using DotnetDiag.Gui.ViewModels;
using DotnetDiag.Gui.Views;

namespace DotnetDiag.Gui.Tests;

/// <summary>Drives the real window through the headless platform with the real Skia renderer.</summary>
public class AppRenderingTests
{
    private const string AiAnswer =
        """
        ## Root cause
        `Shop.Api` calls `JsonConvert`, but the project does not reference **Newtonsoft.Json**. The `CS0103` and `CS0006` errors are consequences.

        ## Fix
        1. Add the package:
        ```bash
        dotnet add src/Shop.Api/Shop.Api.csproj package Newtonsoft.Json
        ```
        2. Or move to the built-in serializer:
        ```diff
        --- a/src/Shop.Api/Services/OrderSerializer.cs
        +++ b/src/Shop.Api/Services/OrderSerializer.cs
        @@ -1,4 +1,4 @@
        -using Newtonsoft.Json;
        +using System.Text.Json;
        ```

        ## Confidence
        high: the package is absent from the project file.
        """;

    private static readonly string[] PageNames = ["analyze", "explain", "doctor", "settings"];

    [Fact]
    public Task Every_page_renders_in_every_theme() => Headless.Run(async () =>
    {
        using var temp = new TempDirectory();
        var (window, viewModel) = Open(temp);
        await viewModel.Analyze.OpenPathAsync(Fixtures.Path("missing-package-cs0246.txt"));
        Assert.NotNull(viewModel.Analyze.Report);

        foreach (var theme in viewModel.Settings.Themes.Where(t => t.Id != ThemeManager.SystemThemeId))
        {
            viewModel.Settings.SelectedTheme = theme;
            Assert.Same(theme.Preview, ThemeManager.Current);

            for (var page = 0; page < PageNames.Length; page++)
            {
                viewModel.SelectedPageIndex = page;
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Screenshots.Save(frame, $"{theme.Id}-{PageNames[page]}");

                if (page == MainWindowViewModel.AnalyzePage)
                {
                    // Rows painted with a tone follow the theme without being rebuilt.
                    var errors = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "5 error(s)");
                    Assert.Equal(theme.Preview.TextColorOf(Tone.Error), Assert.IsAssignableFrom<ISolidColorBrush>(errors.Foreground).Color);

                    // The primary button is filled with the theme's accent itself.
                    var analyze = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("primary"));
                    Assert.Equal(theme.Preview.Accent, Assert.IsAssignableFrom<ISolidColorBrush>(analyze.Background).Color);
                }
            }

            // The window itself is painted from the theme, not from the stock Fluent colors.
            Assert.Equal(theme.Preview.Background, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
        }

        window.Close();
    });

    [Fact]
    public Task Start_screen_and_run_options_render() => Headless.Run(() =>
    {
        using var temp = new TempDirectory();
        var (window, viewModel) = Open(temp);

        Screenshots.Save(window.CaptureRenderedFrame(), "start");
        viewModel.Analyze.OptionsExpanded = true;
        Screenshots.Save(window.CaptureRenderedFrame(), "start-options");

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Drop a build or test log here");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "EQUIVALENT COMMAND");
        window.Close();
    });

    [Fact]
    public Task Ai_diagnosis_is_rendered_as_markdown() => Headless.Run(async () =>
    {
        using var temp = new TempDirectory();
        var (window, viewModel) = Open(temp);
        var result = await new DiagnosisPipeline(new CannedProvider(AiAnswer))
            .AnalyzeFileAsync(Fixtures.Path("missing-package-cs0246.txt"), workspace: null);
        viewModel.Analyze.Report = new ReportViewModel(result, new ReportOptions(), new FakeDesktop());

        window.Height = 1500; // tall enough for the whole report, so the capture shows the diagnosis
        Screenshots.Save(window.CaptureRenderedFrame(), "ai-diagnosis-dark"); // the first frame also builds the visual tree
        var markdown = Assert.Single(window.GetVisualDescendants().OfType<MarkdownView>());
        var rendered = markdown.Children.ToList();

        // Switching themes repaints the rendered answer in place: nothing is rebuilt.
        foreach (var theme in (AppTheme[])[ThemeCatalog.Light, ThemeCatalog.HighContrast])
        {
            viewModel.Settings.SelectedTheme = viewModel.Settings.Themes.Single(t => t.Id == theme.Id);
            Screenshots.Save(window.CaptureRenderedFrame(), $"ai-diagnosis-{theme.Id}");
            Assert.Equal(rendered, markdown.Children);

            // Inline code is a text run, not a control; it still takes the theme's color.
            var code = markdown.Children.OfType<SelectableTextBlock>().SelectMany(t => t.Inlines!).OfType<Run>().First(r => r.Text == "JsonConvert");
            Assert.Equal(theme.TextColorOf(Tone.Mint), Assert.IsAssignableFrom<ISolidColorBrush>(code.Foreground).Color);
        }

        Assert.Equal(2, markdown.Children.OfType<Border>().Count(b => b.Classes.Contains("codeblock")));
        Assert.Contains(markdown.Children.OfType<SelectableTextBlock>(), t => t.Inlines!.Text == "Root cause");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "AI diagnosis · Ollama · canned-model");
        window.Close();
    });

    [Fact]
    public Task Test_failures_and_nuget_conflicts_render() => Headless.Run(async () =>
    {
        using var temp = new TempDirectory();
        var (window, viewModel) = Open(temp);

        await viewModel.Analyze.OpenPathAsync(Fixtures.Path("dotnet-test-vstest.txt"));
        Assert.True(viewModel.Analyze.Report!.HasTests);
        Screenshots.Save(window.CaptureRenderedFrame(), "report-tests");

        await viewModel.Analyze.OpenPathAsync(Fixtures.Path("version-conflict.txt"));
        Assert.True(viewModel.Analyze.Report!.HasVersionConflicts);
        Screenshots.Save(window.CaptureRenderedFrame(), "report-conflicts");
        window.Close();
    });

    [Fact]
    public Task Text_size_and_density_restyle_the_app() => Headless.Run(() =>
    {
        using var temp = new TempDirectory();
        var (window, viewModel) = Open(temp);
        var app = Application.Current!;

        viewModel.Settings.TextScale = 1.5;
        viewModel.Settings.CompactDensity = true;

        Assert.Equal(21d, app.Resources["DiagFontSize"]);
        Assert.Equal(21d, window.FontSize);
        Assert.Equal(new Thickness(12, 5), app.Resources["DiagRowPadding"]);
        Screenshots.Save(window.CaptureRenderedFrame(), "large-text-compact");
        window.Close();
    });

    [Fact]
    public Task Painted_controls_are_released_with_their_report() => Headless.Run(() =>
    {
        // Every report builds hundreds of tone-painted rows. If the theme held on to them, each analysis would
        // leak its whole report.
        var painted = PaintAndForget();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(painted.IsAlive);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference PaintAndForget()
        {
            var block = new TextBlock();
            Paint.SetText(block, Tone.Error);
            var badge = new Border { Child = block };
            Paint.SetBadge(badge, Tone.Accent);
            return new WeakReference(badge);
        }
    });

    [Fact]
    public Task Keyboard_shortcuts_switch_pages() => Headless.Run(() =>
    {
        using var temp = new TempDirectory();
        var (window, viewModel) = Open(temp);

        window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.Control);
        Assert.Equal(MainWindowViewModel.DoctorPage, viewModel.SelectedPageIndex);
        Assert.Same(viewModel.Doctor, viewModel.CurrentPage);

        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Control);
        Assert.Same(viewModel.Analyze, viewModel.CurrentPage);
        window.Close();
    });

    /// <summary>Opens the main window on fresh settings in the brand Dark theme, with AI switched off.</summary>
    private static (MainWindow Window, MainWindowViewModel ViewModel) Open(TempDirectory temp)
    {
        var store = new GuiSettingsStore(temp.File("gui.json"));
        Assert.True(store.TrySave(new GuiSettings { Theme = ThemeCatalog.Dark.Id, AlwaysOffline = true }));

        var window = new MainWindow();
        var viewModel = new MainWindowViewModel(store, new FakeDesktop());
        ((App)Application.Current!).FollowAppearance(viewModel.Settings);
        window.DataContext = viewModel;
        window.Show();
        return (window, viewModel);
    }
}
