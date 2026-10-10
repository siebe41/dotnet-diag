using DotnetDiag.Core.Providers;

namespace DotnetDiag.Tests.Providers;

public class CredentialMaskingTests
{
    [Theory]
    [InlineData("https://alice:hunter2@ollama.internal:11434", "https://***@ollama.internal:11434")]
    [InlineData("http://token@localhost:1234/v1", "http://***@localhost:1234/v1")]
    [InlineData("http://localhost:11434", "http://localhost:11434")]
    [InlineData("not a url", "not a url")]
    public void Masks_user_information_and_leaves_everything_else(string value, string expected) =>
        Assert.Equal(expected, CredentialMasking.WithoutCredentials(value));

    [Fact]
    public void Masks_user_information_that_uri_escapes_differently()
    {
        // Uri reports this user information escaped (%20), so a plain text replacement would miss it.
        var masked = CredentialMasking.WithoutCredentials("https://al ice:hun ter2@host.internal/v1");

        Assert.DoesNotContain("hun", masked);
        Assert.DoesNotContain("ice", masked);
        Assert.Contains("***@host.internal", masked);
    }

    [Fact]
    public void Masks_a_uri()
    {
        var masked = CredentialMasking.WithoutCredentials(new Uri("https://alice:hunter2@ollama.internal:11434"));

        Assert.Equal("https://***@ollama.internal:11434/", masked);
    }
}
