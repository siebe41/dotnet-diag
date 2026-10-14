namespace DotnetDiag.Core.Providers;

/// <summary>Hides credentials in values that are shown to people or copied into bug reports.</summary>
public static class CredentialMasking
{
    /// <summary>
    /// Replaces <c>user:password@</c> in a URL with <c>***@</c>. Anything that is not an absolute URL with user
    /// information is returned unchanged.
    /// </summary>
    public static string WithoutCredentials(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length == 0)
        {
            return value;
        }

        var masked = value.Replace(uri.UserInfo + "@", "***@", StringComparison.Ordinal);
        if (!masked.Contains(uri.UserInfo + "@", StringComparison.Ordinal) && masked != value)
        {
            return masked;
        }

        // The value spells the user information differently from Uri's escaped form (e.g. a space or a raw
        // reserved character): rebuild the URL without it rather than risk showing it.
        return new UriBuilder(uri) { UserName = "***", Password = string.Empty }.Uri.ToString();
    }

    /// <inheritdoc cref="WithoutCredentials(string)"/>
    public static string WithoutCredentials(Uri uri) => WithoutCredentials(uri.ToString());
}
