namespace DotnetDiag.Core.Parsing;

/// <summary>Removes terminal/CI noise from log lines so the parsers (and the LLM) only see content.</summary>
public static class TextSanitizer
{
    public static string CleanLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var cleaned = Patterns.AnsiEscape().Replace(line, string.Empty);
        cleaned = cleaned.TrimEnd('\r');
        cleaned = Patterns.LeadingTimestamp().Replace(cleaned, string.Empty);
        cleaned = Patterns.CiAnnotation().Replace(cleaned, string.Empty);
        return cleaned;
    }

    public static IEnumerable<string> CleanLines(string text) =>
        text.Split('\n').Select(CleanLine);

    /// <summary>Truncates to <paramref name="maxLength"/> characters, appending an ellipsis.</summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return maxLength <= 1 ? "…" : string.Concat(text.AsSpan(0, maxLength - 1).TrimEnd(), "…");
    }

    /// <summary>Collapses runs of whitespace to a single space.</summary>
    public static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
