namespace DotnetDiag.Cli.Commands;

internal static class ExitCodes
{
    public const int Success = 0;

    /// <summary>Returned by <c>analyze --exit-code</c> when errors or failing tests were found.</summary>
    public const int ProblemsFound = 1;

    public const int InvalidInput = 2;
}
