using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace DotnetDiag.Gui.Services;

/// <summary>What view models need from the desktop: pickers, the clipboard and the OS shell.</summary>
public interface IDesktopServices
{
    Task<string?> PickLogFileAsync();

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension);

    Task CopyTextAsync(string text);

    Task<string?> ReadClipboardTextAsync();

    /// <summary>Opens an http(s) link in the browser. Anything else is ignored.</summary>
    void OpenUri(string uri);

    /// <summary>Opens a folder in the file manager.</summary>
    void OpenFolder(string path);

    /// <summary>
    /// Opens a source or text file with its default app and returns true; returns false for anything else.
    /// Paths come from logs, which are untrusted input: a log must never be able to start a program.
    /// </summary>
    bool TryOpenSourceFile(string path);
}

/// <summary><see cref="IDesktopServices"/> on top of the main window.</summary>
public sealed class WindowDesktopServices(TopLevel topLevel) : IDesktopServices
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".fsx", ".vb", ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".sln", ".slnx",
        ".json", ".xml", ".config", ".razor", ".cshtml", ".xaml", ".axaml", ".resx", ".editorconfig",
        ".txt", ".log", ".md", ".yml", ".yaml",
    };

    public async Task<string?> PickLogFileAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a build or test log",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Build and test logs") { Patterns = ["*.binlog", "*.log", "*.txt"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }],
        });
        return file?.TryGetLocalPath();
    }

    // The clipboard belongs to the OS and can be locked by another process at any moment; each platform reports
    // that with its own exception type. A failed copy or paste must not take the app down.
    public async Task CopyTextAsync(string text)
    {
        try
        {
            if (topLevel.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception)
        {
        }
    }

    public async Task<string?> ReadClipboardTextAsync()
    {
        try
        {
            return topLevel.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void OpenUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            _ = topLevel.Launcher.LaunchUriAsync(parsed);
        }
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            _ = topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
    }

    public bool TryOpenSourceFile(string path)
    {
        if (!SourceExtensions.Contains(Path.GetExtension(path)) || !File.Exists(path))
        {
            return false;
        }

        _ = topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        return true;
    }
}
