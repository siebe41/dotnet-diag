using System.Text;
using System.Text.RegularExpressions;

namespace DotnetDiag.Gui.Markdown;

public abstract record MarkdownBlock;

public sealed record HeadingBlock(int Level, string Text) : MarkdownBlock;

public sealed record ParagraphBlock(string Text) : MarkdownBlock;

/// <param name="Indent">Leading spaces of the item, for nested lists.</param>
/// <param name="Number">The item number for ordered lists, null for bullets.</param>
public sealed record ListItemBlock(int Indent, string? Number, string Text) : MarkdownBlock;

public sealed record QuoteBlock(string Text) : MarkdownBlock;

public sealed record CodeBlock(string Language, IReadOnlyList<string> Lines) : MarkdownBlock
{
    public bool IsDiff => Language.Equals("diff", StringComparison.OrdinalIgnoreCase) || Language.Equals("patch", StringComparison.OrdinalIgnoreCase);

    public string Text => string.Join('\n', Lines);
}

public enum InlineKind
{
    Text,
    Code,
    Bold,
    Italic,
    Link,
}

public sealed record MarkdownInline(InlineKind Kind, string Text, string? Url = null);

/// <summary>
/// Parses the Markdown subset LLMs produce (headings, lists, quotes, fenced code, inline code, bold, italics,
/// links): the same subset the CLI renders in the terminal.
/// </summary>
public static partial class MarkdownParser
{
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new ParagraphBlock(paragraph.ToString().Trim()));
                paragraph.Clear();
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Add(lines[i]);
                }

                blocks.Add(new CodeBlock(language, code));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            var heading = Heading().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph();
                blocks.Add(new HeadingBlock(heading.Groups["level"].Value.Length, heading.Groups["text"].Value));
                continue;
            }

            var bullet = Bullet().Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                blocks.Add(new ListItemBlock(
                    bullet.Groups["indent"].Value.Length,
                    bullet.Groups["num"].Success ? bullet.Groups["num"].Value : null,
                    bullet.Groups["text"].Value));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                blocks.Add(new QuoteBlock(trimmed.TrimStart('>').Trim()));
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>Splits a line into plain text and formatted spans. Unbalanced markers stay plain text.</summary>
    public static IReadOnlyList<MarkdownInline> ParseInline(string text)
    {
        var parts = new List<MarkdownInline>();
        var last = 0;

        void AddText(string value)
        {
            if (value.Length > 0)
            {
                parts.Add(new MarkdownInline(InlineKind.Text, value));
            }
        }

        foreach (Match match in InlineToken().Matches(text))
        {
            AddText(text[last..match.Index]);
            if (match.Groups["code"].Success)
            {
                parts.Add(new MarkdownInline(InlineKind.Code, match.Groups["code"].Value));
            }
            else if (match.Groups["bold"].Success)
            {
                parts.Add(new MarkdownInline(InlineKind.Bold, match.Groups["bold"].Value));
            }
            else if (match.Groups["italic"].Success)
            {
                parts.Add(new MarkdownInline(InlineKind.Italic, match.Groups["italic"].Value));
            }
            else if (match.Groups["linktext"].Success)
            {
                parts.Add(new MarkdownInline(InlineKind.Link, match.Groups["linktext"].Value, match.Groups["url"].Value));
            }

            last = match.Index + match.Length;
        }

        AddText(text[last..]);
        return parts;
    }

    [GeneratedRegex(@"^(?<level>#{1,6})\s+(?<text>.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?<indent>\s*)(?:[-*+]|(?<num>\d+)[.)])\s+(?<text>.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|__(?<bold>[^_]+)__|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|\[(?<linktext>[^\]]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex InlineToken();
}
