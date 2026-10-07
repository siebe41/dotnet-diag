using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetDiag.Cli.Rendering;

/// <summary>
/// Renders the Markdown subset LLMs produce (headings, lists, quotes, fenced code, diffs, inline code,
/// bold, italics, links) as Spectre.Console renderables.
/// </summary>
internal static partial class MarkdownRenderer
{
    public static IRenderable Render(string markdown)
    {
        var items = new List<IRenderable>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                items.Add(new Markup(Inline(paragraph.ToString().Trim())));
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

                items.Add(CodeBlock(language, code));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                if (items.Count > 0 && items[^1] is not Text { Length: 0 })
                {
                    items.Add(Text.Empty);
                }

                continue;
            }

            var heading = Heading().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph();
                var text = Inline(heading.Groups["text"].Value);
                items.Add(heading.Groups["level"].Value.Length <= 2
                    ? new Rule($"[bold deepskyblue1]{text}[/]") { Justification = Justify.Left, Style = Theme.Border }
                    : new Markup($"[bold underline]{text}[/]"));
                continue;
            }

            var bullet = Bullet().Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                var indent = new string(' ', Math.Min(bullet.Groups["indent"].Value.Length, 8));
                var marker = bullet.Groups["num"].Success ? $"[deepskyblue1]{bullet.Groups["num"].Value}.[/]" : "[deepskyblue1]•[/]";
                items.Add(new Markup($"{indent}{marker} {Inline(bullet.Groups["text"].Value)}"));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                items.Add(new Markup($"[grey]│[/] [italic grey]{Inline(trimmed.TrimStart('>').Trim())}[/]"));
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

        FlushParagraph();
        while (items.Count > 0 && items[^1] is Text { Length: 0 })
        {
            items.RemoveAt(items.Count - 1);
        }

        return new Rows(items);
    }

    /// <summary>Converts inline Markdown to Spectre markup, escaping everything that is not formatting.</summary>
    public static string Inline(string text)
    {
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match match in InlineToken().Matches(text))
        {
            sb.Append(Markup.Escape(text[last..match.Index]));
            if (match.Groups["code"].Success)
            {
                sb.Append("[aqua]").Append(Markup.Escape(match.Groups["code"].Value)).Append("[/]");
            }
            else if (match.Groups["bold"].Success)
            {
                sb.Append("[bold]").Append(Markup.Escape(match.Groups["bold"].Value)).Append("[/]");
            }
            else if (match.Groups["italic"].Success)
            {
                sb.Append("[italic]").Append(Markup.Escape(match.Groups["italic"].Value)).Append("[/]");
            }
            else if (match.Groups["linktext"].Success)
            {
                var url = match.Groups["url"].Value;
                var safeUrl = url.Contains(' ', StringComparison.Ordinal) || url.Contains(']', StringComparison.Ordinal) ? null : url;
                sb.Append(safeUrl is null
                    ? Markup.Escape(match.Groups["linktext"].Value)
                    : $"[link={Markup.Escape(safeUrl)}]{Markup.Escape(match.Groups["linktext"].Value)}[/]");
            }

            last = match.Index + match.Length;
        }

        sb.Append(Markup.Escape(text[last..]));
        return sb.ToString();
    }

    private static Panel CodeBlock(string language, IReadOnlyList<string> code)
    {
        var isDiff = language.Equals("diff", StringComparison.OrdinalIgnoreCase) || language.Equals("patch", StringComparison.OrdinalIgnoreCase);
        var rendered = code.Select(line =>
        {
            var escaped = Markup.Escape(line);
            if (!isDiff)
            {
                return $"[white]{escaped}[/]";
            }

            return line switch
            {
                _ when line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) => $"[bold]{escaped}[/]",
                _ when line.StartsWith('+') => $"[green]{escaped}[/]",
                _ when line.StartsWith('-') => $"[red]{escaped}[/]",
                _ when line.StartsWith("@@", StringComparison.Ordinal) => $"[aqua]{escaped}[/]",
                _ => $"[grey]{escaped}[/]",
            };
        });

        var panel = new Panel(new Markup(string.Join('\n', rendered)))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.Border,
            Expand = true,
            Padding = new Padding(1, 0),
        };
        if (language.Length > 0)
        {
            panel.Header = new PanelHeader($"[grey]{Markup.Escape(language)}[/]");
        }

        return panel;
    }

    [GeneratedRegex(@"^(?<level>#{1,6})\s+(?<text>.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?<indent>\s*)(?:[-*+]|(?<num>\d+)[.)])\s+(?<text>.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|__(?<bold>[^_]+)__|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|\[(?<linktext>[^\]]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex InlineToken();
}
