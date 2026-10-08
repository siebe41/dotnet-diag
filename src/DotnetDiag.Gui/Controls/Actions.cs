using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace DotnetDiag.Gui.Controls;

/// <summary>Attached button behaviors for things that are pure UI: copy a text, open a link.</summary>
public static class Actions
{
    /// <summary>Clicking the button copies this text and flashes the "copied" style class.</summary>
    public static readonly AttachedProperty<string?> CopyTextProperty =
        AvaloniaProperty.RegisterAttached<Button, string?>("CopyText", typeof(Actions));

    /// <summary>Clicking the button opens this http(s) link in the browser.</summary>
    public static readonly AttachedProperty<string?> OpenUrlProperty =
        AvaloniaProperty.RegisterAttached<Button, string?>("OpenUrl", typeof(Actions));

    private static readonly TimeSpan CopiedFlash = TimeSpan.FromSeconds(1.2);

    static Actions() => Button.ClickEvent.AddClassHandler<Button>(OnClick);

    public static string? GetCopyText(Button element) => element.GetValue(CopyTextProperty);

    public static void SetCopyText(Button element, string? value) => element.SetValue(CopyTextProperty, value);

    public static string? GetOpenUrl(Button element) => element.GetValue(OpenUrlProperty);

    public static void SetOpenUrl(Button element, string? value) => element.SetValue(OpenUrlProperty, value);

    private static async void OnClick(Button button, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(button);
        if (topLevel is null)
        {
            return;
        }

        try
        {
            if (GetCopyText(button) is { Length: > 0 } text && topLevel.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                button.Classes.Add("copied");
                await Task.Delay(CopiedFlash);
                button.Classes.Remove("copied");
            }
            else if (GetOpenUrl(button) is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                await topLevel.Launcher.LaunchUriAsync(uri);
            }
        }
        catch (Exception)
        {
            // The clipboard is held by another process, or no browser is registered. Each platform reports that with
            // its own exception type, and an exception leaving this async void handler would take the app down.
        }
    }
}
