using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using DotnetDiag.Gui.Markdown;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.Controls;

/// <summary>Shows Markdown (an AI diagnosis) as themed text, lists and copyable code blocks.</summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    static MarkdownView() => MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Rebuild());

    public MarkdownView() => Spacing = 8;

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var block in MarkdownParser.Parse(Markdown ?? string.Empty))
        {
            Children.Add(Render(block));
        }
    }

    private static Control Render(MarkdownBlock block) => block switch
    {
        HeadingBlock heading => Heading(heading),
        ListItemBlock item => ListItem(item),
        QuoteBlock quote => Quote(quote),
        CodeBlock code => Code(code),
        ParagraphBlock paragraph => Text(paragraph.Text),
        _ => new Panel(),
    };

    private static SelectableTextBlock Text(string text)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        InlineMarkdown.Apply(block, text);
        return block;
    }

    private static Control Heading(HeadingBlock heading)
    {
        var block = Text(heading.Text);
        block.Margin = new Thickness(0, 6, 0, 0);
        if (heading.Level <= 2)
        {
            block.Classes.Add("h3");
            Paint.SetText(block, Tone.Accent);
        }
        else
        {
            block.Classes.Add("strong");
        }

        return block;
    }

    private static Control ListItem(ListItemBlock item)
    {
        var marker = new TextBlock { Text = item.Number is null ? "•" : item.Number + ".", MinWidth = 20, Margin = new Thickness(0, 0, 6, 0) };
        Paint.SetText(marker, Tone.Accent);

        var text = Text(item.Text);
        Grid.SetColumn(text, 1);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(Math.Min(item.Indent, 8) * 8, 0, 0, 0),
            Children = { marker, text },
        };
    }

    private static Control Quote(QuoteBlock quote)
    {
        var text = Text(quote.Text);
        text.FontStyle = FontStyle.Italic;
        text.Classes.Add("muted");

        var border = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Child = text };
        Paint.BindResource(border, Border.BorderBrushProperty, "DiagBorderStrongBrush");
        return border;
    }

    private static Control Code(CodeBlock code)
    {
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.NoWrap, Inlines = [] };
        text.Classes.Add("mono");
        for (var i = 0; i < code.Lines.Count; i++)
        {
            if (i > 0)
            {
                text.Inlines.Add(new LineBreak());
            }

            text.Inlines.Add(code.IsDiff ? DiffLine(code.Lines[i]) : new Run(code.Lines[i]));
        }

        var language = new TextBlock { Text = code.Language.ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center };
        language.Classes.Add("label");

        var copy = new Button { Content = "Copy" };
        copy.Classes.Add("ghost");
        copy.Classes.Add("small");
        Actions.SetCopyText(copy, code.Text);
        ToolTip.SetTip(copy, "Copy this block");
        DockPanel.SetDock(copy, Dock.Right);

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = text,
        };
        DockPanel.SetDock(body, Dock.Bottom);

        var border = new Border { Child = new DockPanel { Children = { body, copy, language } } };
        border.Classes.Add("codeblock");
        return border;
    }

    private static Run DiffLine(string line)
    {
        var run = new Run(line);
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
        {
            run.FontWeight = FontWeight.Bold;
        }
        else if (line.StartsWith('+'))
        {
            Paint.Foreground(run, Tone.Success);
        }
        else if (line.StartsWith('-'))
        {
            Paint.Foreground(run, Tone.Error);
        }
        else if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            Paint.Foreground(run, Tone.Accent);
        }
        else
        {
            Paint.Foreground(run, Tone.Neutral);
        }

        return run;
    }
}

/// <summary>
/// Inline Markdown (code, bold, italics, links) for any TextBlock. The offline hints use backticks for
/// commands and identifiers, so their text goes through this rather than a plain Text binding.
/// </summary>
public static class InlineMarkdown
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(InlineMarkdown));

    static InlineMarkdown() =>
        TextProperty.Changed.AddClassHandler<TextBlock>((block, e) => Apply(block, e.GetNewValue<string?>()));

    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);

    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);

    public static void Apply(TextBlock block, string? text)
    {
        var inlines = block.Inlines ??= [];
        inlines.Clear();
        foreach (var part in MarkdownParser.ParseInline(text ?? string.Empty))
        {
            inlines.AddRange(ToRuns(part));
        }
    }

    private static IEnumerable<Run> ToRuns(MarkdownInline part)
    {
        switch (part.Kind)
        {
            case InlineKind.Code:
                var code = new Run(part.Text) { FontFamily = ThemeManager.MonoFont };
                Paint.Foreground(code, Tone.Mint);
                yield return code;
                break;

            case InlineKind.Bold:
                yield return new Run(part.Text) { FontWeight = FontWeight.Bold };
                break;

            case InlineKind.Italic:
                yield return new Run(part.Text) { FontStyle = FontStyle.Italic };
                break;

            case InlineKind.Link:
                // Model output is untrusted: links are shown with their target, never made clickable.
                var link = new Run(part.Text) { TextDecorations = TextDecorations.Underline };
                Paint.Foreground(link, Tone.Accent);
                yield return link;
                if (!string.Equals(part.Text, part.Url, StringComparison.OrdinalIgnoreCase))
                {
                    var target = new Run($" ({part.Url})");
                    Paint.Foreground(target, Tone.Neutral);
                    yield return target;
                }

                break;

            default:
                yield return new Run(part.Text);
                break;
        }
    }
}
