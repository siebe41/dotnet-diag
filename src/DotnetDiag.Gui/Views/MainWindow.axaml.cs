using Avalonia.Controls;
using DotnetDiag.Gui.ViewModels;

namespace DotnetDiag.Gui.Views;

public partial class MainWindow : Window
{
    private bool _placed;

    public MainWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_placed || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _placed = true;
        var settings = viewModel.Settings.Current;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            // A maximized window reports the screen size: keep the last restored size for the next start.
            var maximized = WindowState == WindowState.Maximized;
            var previous = viewModel.Settings.Current;
            viewModel.Settings.SaveWindow(
                maximized ? previous.WindowWidth : ClientSize.Width,
                maximized ? previous.WindowHeight : ClientSize.Height,
                maximized);
        }
    }
}
