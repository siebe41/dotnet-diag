namespace DotnetDiag.Core.Context;

/// <summary>
/// Cheap, provider-agnostic token estimate. Build logs and code are token-dense (paths, punctuation),
/// so this deliberately over-estimates compared to the usual ~4 characters per token for prose.
/// </summary>
public static class TokenEstimator
{
    public const double CharactersPerToken = 3.5;

    public static int Estimate(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharactersPerToken);

    public static int Estimate(int characters) =>
        characters <= 0 ? 0 : (int)Math.Ceiling(characters / CharactersPerToken);
}
