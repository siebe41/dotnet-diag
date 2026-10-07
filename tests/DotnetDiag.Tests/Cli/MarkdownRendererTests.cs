using DotnetDiag.Cli.Rendering;
using Spectre.Console;

namespace DotnetDiag.Tests.Cli;

public class MarkdownRendererTests
{
    [Theory]
    [InlineData("plain [Fact] text", "plain [[Fact]] text")]
    [InlineData("run `dotnet add package X`", "run [aqua]dotnet add package X[/]")]
    [InlineData("**Root cause**: missing", "[bold]Root cause[/]: missing")]
    [InlineData("an *important* note", "an [italic]important[/] note")]
    [InlineData("snake_case_names stay", "snake_case_names stay")]
    [InlineData("a * b * c", "a * b * c")]
    [InlineData("see [docs](https://learn.microsoft.com)", "see [link=https://learn.microsoft.com]docs[/]")]
    [InlineData("`List<int>[0]`", "[aqua]List<int>[[0]][/]")]
    public void Converts_inline_markdown_to_safe_markup(string markdown, string expected)
    {
        var markup = MarkdownRenderer.Inline(markdown);

        Assert.Equal(expected, markup);
        _ = new Markup(markup); // must be valid Spectre markup
    }

    [Fact]
    public void Renders_llm_style_markdown_without_markup_errors()
    {
        const string markdown = """
            ## Root cause
            The project uses `JsonConvert` but does not reference **Newtonsoft.Json** [sic].

            ## Fix
            1. Add the package:
            ```bash
            dotnet add src/Shop.Api/Shop.Api.csproj package Newtonsoft.Json
            ```
            2. Or switch to System.Text.Json:
            ```diff
            --- a/src/Shop.Api/Services/OrderSerializer.cs
            +++ b/src/Shop.Api/Services/OrderSerializer.cs
            @@ -3,1 +3,1 @@
            -using Newtonsoft.Json;
            +using System.Text.Json;
            ```
            > Note: [Fact] attributes are unaffected.

            ## Confidence
            - high
            """;

        var output = RenderToString(MarkdownRenderer.Render(markdown));

        Assert.Contains("Root cause", output);
        Assert.Contains("dotnet add src/Shop.Api/Shop.Api.csproj package Newtonsoft.Json", output);
        Assert.Contains("+using System.Text.Json;", output);
        Assert.Contains("[Fact] attributes", output);
        Assert.Contains("• high", output);
    }

    internal static string RenderToString(Spectre.Console.Rendering.IRenderable renderable)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = 140;
        console.Write(renderable);
        return writer.ToString();
    }
}
