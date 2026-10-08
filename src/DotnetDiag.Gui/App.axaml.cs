using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;
using DotnetDiag.Gui.ViewModels;
using DotnetDiag.Gui.Views;

namespace DotnetDiag.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Views resolve Diag* resources from the first frame, so a theme must be in place before any window exists.
        ThemeManager.Apply(this, ThemeCatalog.Dark);
        ThemeManager.ApplyMetrics(this, textScale: 1, compact: false);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(
                new GuiSettingsStore(GuiSettingsStore.DefaultPath),
                new WindowDesktopServices(window),
                () => SystemTheme);
            FollowAppearance(viewModel.Settings);

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // `dotnet-diag-gui build.binlog`, or a log opened with the app from the file manager.
            if (desktop.Args is [{ Length: > 0 } input, ..])
            {
                Dispatcher.UIThread.Post(() => _ = viewModel.Analyze.OpenPathAsync(input));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private AppTheme SystemTheme => ThemeManager.Resolve(ThemeManager.SystemThemeId, PlatformSettings?.GetColorValues());

    /// <summary>Applies the saved appearance now, on every change, and when the OS switches light/dark or contrast.</summary>
    public void FollowAppearance(SettingsViewModel settings)
    {
        void Apply()
        {
            var current = settings.Current;
            ThemeManager.Apply(this, ThemeManager.Resolve(current.Theme, PlatformSettings?.GetColorValues()));
            ThemeManager.ApplyMetrics(this, current.TextScale, current.CompactDensity);
        }

        Apply();
        settings.AppearanceChanged += Apply;
        if (PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) => Apply();
        }
    }
}
