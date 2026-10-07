using System.Text;

namespace DotnetDiag.Core.Parsing;

/// <summary>
/// Decodes log bytes without trusting platform defaults: Windows PowerShell 5.1 redirects write UTF-16,
/// pwsh and the .NET CLI write UTF-8 (with or without BOM), older tools write an ANSI code page, and
/// redirected stdin on Windows is otherwise decoded with the OEM code page.
/// </summary>
public static class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            return Encoding.UTF32.GetString(bytes[4..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: an ANSI code page. Latin-1 maps every byte and keeps ASCII (paths, codes) intact.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static string ReadFile(string path) => Decode(File.ReadAllBytes(path));

    public static async Task<string> ReadStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}
