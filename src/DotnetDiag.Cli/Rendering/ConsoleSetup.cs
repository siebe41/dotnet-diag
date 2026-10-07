using Spectre.Console;

namespace DotnetDiag.Cli.Rendering;

internal static class ConsoleSetup
{
    private const int RedirectedWidth = 120;

    /// <summary>
    /// CI logs and pipes report no (or a bogus) terminal width; render at a fixed width there so tables stay
    /// readable. COLUMNS, when set, always wins.
    /// </summary>
    public static void Configure(IAnsiConsole console)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var columns) && columns >= 40)
        {
            console.Profile.Width = columns;
        }
        else if (Console.IsOutputRedirected || console.Profile.Width < 40)
        {
            console.Profile.Width = RedirectedWidth;
        }
    }
}
