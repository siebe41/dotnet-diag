using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotnetDiag.Gui.Services;
using DotnetDiag.Gui.Settings;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.ViewModels;

/// <summary>The shell: navigation between the four pages and the status bar.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public const int AnalyzePage = 0;
    public const int ExplainPage = 1;
    public const int DoctorPage = 2;
    public const int SettingsPage = 3;

    [ObservableProperty]
    private int _selectedPageIndex;

    [ObservableProperty]
    private object _currentPage;

    public MainWindowViewModel(GuiSettingsStore store, IDesktopServices desktop, Func<AppTheme>? systemTheme = null)
    {
        Settings = new SettingsViewModel(store, desktop, systemTheme: systemTheme);
        Analyze = new AnalyzeViewModel(Settings, desktop);
        Explain = new ExplainViewModel();
        Doctor = new DoctorViewModel(Analyze.BuildRequest, desktop);
        _currentPage = Analyze;

        Analyze.ExplainRequested += code =>
        {
            Explain.Show(code);
            SelectedPageIndex = ExplainPage;
        };
        Doctor.SettingsRequested += () => SelectedPageIndex = SettingsPage;
        Settings.ProviderConfigSaved += () =>
        {
            Analyze.RefreshDerived();
            Doctor.Refresh();
        };
    }

    public AnalyzeViewModel Analyze { get; }

    public ExplainViewModel Explain { get; }

    public DoctorViewModel Doctor { get; }

    public SettingsViewModel Settings { get; }

    public string Version { get; } = "v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    partial void OnSelectedPageIndexChanged(int value)
    {
        if (value == DoctorPage)
        {
            // Run options or environment may have changed since the page was last shown.
            Doctor.Refresh();
        }

        CurrentPage = value switch
        {
            ExplainPage => Explain,
            DoctorPage => Doctor,
            SettingsPage => Settings,
            _ => Analyze,
        };
    }

    /// <summary>Keyboard navigation (Ctrl+1 … Ctrl+4).</summary>
    [RelayCommand]
    private void Navigate(string page)
    {
        if (int.TryParse(page, out var index) && index is >= AnalyzePage and <= SettingsPage)
        {
            SelectedPageIndex = index;
        }
    }
}
