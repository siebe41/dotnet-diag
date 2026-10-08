using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DotnetDiag.Gui.ViewModels;

namespace DotnetDiag.Gui.Views;

public partial class AnalyzeView : UserControl
{
    public AnalyzeView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    private static bool CanAccept(DragEventArgs e) =>
        e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(DataFormat.Text);

    private void OnDragEnter(object? sender, DragEventArgs e) => DropOverlay.IsVisible = CanAccept(e);

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = CanAccept(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (DataContext is not AnalyzeViewModel viewModel)
        {
            return;
        }

        if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            await viewModel.OpenPathAsync(path);
        }
        else if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
        {
            await viewModel.OpenTextAsync(text);
        }
    }

    /// <summary>Ctrl+V anywhere on the page except inside a text box pastes a log.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control && e.Source is not TextBox
            && DataContext is AnalyzeViewModel viewModel && viewModel.PasteCommand.CanExecute(null))
        {
            viewModel.PasteCommand.Execute(null);
            e.Handled = true;
        }
    }
}
