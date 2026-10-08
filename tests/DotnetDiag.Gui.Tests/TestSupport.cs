using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using DotnetDiag.Core.Context;
using DotnetDiag.Core.Providers;
using DotnetDiag.Gui.Services;

namespace DotnetDiag.Gui.Tests;

internal static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

/// <summary>Keeps the tests away from the developer's real configuration, provider and API keys.</summary>
internal static class TestEnvironment
{
    static TestEnvironment()
    {
        string[] variables =
        [
            "DOTNET_DIAG_PROVIDER", "DOTNET_DIAG_MODEL", "DOTNET_DIAG_ENDPOINT", "DOTNET_DIAG_API_KEY", "DOTNET_DIAG_EFFORT",
            "DOTNET_DIAG_TIMEOUT", "DOTNET_DIAG_MAX_OUTPUT_TOKENS", "ANTHROPIC_API_KEY", "ANTHROPIC_BASE_URL",
            "OPENAI_API_KEY", "OPENAI_BASE_URL", "OLLAMA_HOST",
        ];
        foreach (var name in variables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable(
            "DOTNET_DIAG_CONFIG",
            Path.Combine(Path.GetTempPath(), "dotnet-diag-gui-tests", $"no-config-{Guid.NewGuid():N}.json"));
    }

    /// <summary>Call before anything that resolves provider settings.</summary>
    public static void Isolate()
    {
    }
}

/// <summary>The app as the tests run it: no window system, but the real Skia renderer so frames can be captured.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

/// <summary>Runs test code on the UI thread of one shared headless Avalonia session.</summary>
internal static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() =>
    {
        TestEnvironment.Isolate();
        return HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
    });

    public static Task Run(Action action) => Session.Value.Dispatch(action, CancellationToken.None);

    public static Task Run(Func<Task> action) => Session.Value.Dispatch(
        async () =>
        {
            await action();
            return true;
        },
        CancellationToken.None);
}

/// <summary>Records what the app asked the desktop to do, and answers pickers with preset values.</summary>
internal sealed class FakeDesktop : IDesktopServices
{
    public FakeDesktop() => TestEnvironment.Isolate();

    public string? FileToPick { get; set; }

    public string? FolderToPick { get; set; }

    public string? SavePath { get; set; }

    public string? Clipboard { get; set; }

    public List<string> OpenedUris { get; } = [];

    public List<string> OpenedFolders { get; } = [];

    public List<string> OpenedFiles { get; } = [];

    public Task<string?> PickLogFileAsync() => Task.FromResult(FileToPick);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderToPick);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult(SavePath);

    public Task CopyTextAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }

    public Task<string?> ReadClipboardTextAsync() => Task.FromResult(Clipboard);

    public void OpenUri(string uri) => OpenedUris.Add(uri);

    public void OpenFolder(string path) => OpenedFolders.Add(path);

    public bool TryOpenSourceFile(string path)
    {
        OpenedFiles.Add(path);
        return true;
    }
}

/// <summary>A model that always gives the same answer, so AI rendering can be tested without a network.</summary>
internal sealed class CannedProvider(string answer) : ILlmProvider
{
    public ProviderKind Kind => ProviderKind.Ollama;

    public string Model => "canned-model";

    public Task<LlmResponse> CompleteAsync(LlmPrompt prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse(answer, Model, InputTokens: 512, OutputTokens: 164));
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        TestEnvironment.Isolate();
        Root = Path.Combine(Path.GetTempPath(), "dotnet-diag-gui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Writes captured frames to the folder in DOTNET_DIAG_GUI_SCREENSHOTS; does nothing when it is not set.</summary>
internal static class Screenshots
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("DOTNET_DIAG_GUI_SCREENSHOTS");

    public static void Save(Bitmap? frame, string name)
    {
        if (frame is null || string.IsNullOrEmpty(Folder))
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        frame.Save(Path.Combine(Folder, name + ".png"), new PngBitmapEncoderOptions());
    }
}
