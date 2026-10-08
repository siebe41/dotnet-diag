using DotnetDiag.Gui.Markdown;

namespace DotnetDiag.Gui.Tests;

public class MarkdownParserTests
{
    [Fact]
    public void Parses_the_sections_a_model_answers_with()
    {
        var blocks = MarkdownParser.Parse(
            """
            ## Root cause
            The project does not reference
            Newtonsoft.Json.

            ## Fix
            1. Add the package:
            ```bash
            dotnet add package Newtonsoft.Json
            ```
              - nested note
            > Run restore afterwards.
            """);

        Assert.Collection(
            blocks,
            b => Assert.Equal(new HeadingBlock(2, "Root cause"), b),
            b => Assert.Equal(new ParagraphBlock("The project does not reference Newtonsoft.Json."), b),
            b => Assert.Equal(new HeadingBlock(2, "Fix"), b),
            b => Assert.Equal(new ListItemBlock(0, "1", "Add the package:"), b),
            b =>
            {
                var code = Assert.IsType<CodeBlock>(b);
                Assert.Equal("bash", code.Language);
                Assert.False(code.IsDiff);
                Assert.Equal("dotnet add package Newtonsoft.Json", code.Text);
            },
            b => Assert.Equal(new ListItemBlock(2, null, "nested note"), b),
            b => Assert.Equal(new QuoteBlock("Run restore afterwards."), b));
    }

    [Theory]
    [InlineData("diff", true)]
    [InlineData("PATCH", true)]
    [InlineData("csharp", false)]
    [InlineData("", false)]
    public void Recognizes_diff_blocks(string language, bool isDiff)
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(MarkdownParser.Parse($"```{language}\n-old\n+new\n```")));

        Assert.Equal(isDiff, code.IsDiff);
        Assert.Equal(["-old", "+new"], code.Lines);
    }

    [Fact]
    public void An_unterminated_code_block_keeps_its_lines()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(MarkdownParser.Parse("```\nline 1\nline 2")));

        Assert.Equal(["line 1", "line 2"], code.Lines);
    }

    [Fact]
    public void Splits_inline_formatting()
    {
        var parts = MarkdownParser.ParseInline("Run `dotnet restore`, then **rebuild** the *whole* solution ([docs](https://example.test/a)).");

        Assert.Equal(
            [
                new MarkdownInline(InlineKind.Text, "Run "),
                new MarkdownInline(InlineKind.Code, "dotnet restore"),
                new MarkdownInline(InlineKind.Text, ", then "),
                new MarkdownInline(InlineKind.Bold, "rebuild"),
                new MarkdownInline(InlineKind.Text, " the "),
                new MarkdownInline(InlineKind.Italic, "whole"),
                new MarkdownInline(InlineKind.Text, " solution ("),
                new MarkdownInline(InlineKind.Link, "docs", "https://example.test/a"),
                new MarkdownInline(InlineKind.Text, ")."),
            ],
            parts);
    }

    [Theory]
    [InlineData("a * b * c")]
    [InlineData("List<T> and x*y")]
    [InlineData("an unclosed `backtick")]
    [InlineData("")]
    public void Text_that_only_looks_like_markup_stays_plain(string text)
    {
        var parts = MarkdownParser.ParseInline(text);

        Assert.All(parts, p => Assert.Equal(InlineKind.Text, p.Kind));
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text)));
    }
}
