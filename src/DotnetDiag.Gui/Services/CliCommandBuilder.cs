using System.Globalization;
using DotnetDiag.Core.Context;

namespace DotnetDiag.Gui.Services;

/// <summary>Renders an analysis as the equivalent <c>dotnet-diag analyze</c> command line, e.g. to paste into a CI script.</summary>
public static class CliCommandBuilder
{
    private static readonly int DefaultTokenBudget = new ContextBuilderOptions().MaxTokens;

    /// <param name="format">Value for <c>--format</c> (markdown, json), or null for the default terminal output.</param>
    public static string Build(AnalysisRequest request, bool showWarnings = false, bool exitCode = false, string? format = null)
    {
        var parts = new List<string> { "dotnet-diag", "analyze" };
        var pasted = request.PastedText is not null;
        if (!pasted && !string.IsNullOrWhiteSpace(request.InputPath))
        {
            parts.Add(Quote(request.InputPath.Trim().Trim('"')));
        }

        if (request.Offline)
        {
            parts.Add("--offline");
        }
        else
        {
            // The API key is left out on purpose: command lines end up in shell history and CI logs.
            var overrides = request.Overrides;
            Add("--provider", overrides.Kind?.ToString().ToLowerInvariant());
            Add("--model", overrides.Model);
            Add("--endpoint", overrides.Endpoint);
            Add("--effort", overrides.Effort);
            Add("--timeout", overrides.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture));
        }

        Add("--repo", request.Repository);
        if (request.Context.MaxTokens != DefaultTokenBudget)
        {
            Add("--max-context-tokens", request.Context.MaxTokens.ToString(CultureInfo.InvariantCulture));
        }

        if (showWarnings)
        {
            parts.Add("--warnings");
        }

        if (exitCode)
        {
            parts.Add("--exit-code");
        }

        Add("--format", format);

        var command = string.Join(' ', parts);
        return pasted ? $"<command> 2>&1 | {command}" : command;

        void Add(string option, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(option);
                parts.Add(Quote(value.Trim()));
            }
        }
    }

    /// <summary>
    /// Quotes a value for the shell the command will most likely be pasted into. File names come from disk and
    /// can contain anything, so everything outside a small safe set is quoted, and <c>$</c> and backticks, which
    /// still expand inside double quotes, force single quotes.
    /// </summary>
    internal static string Quote(string value)
    {
        var windows = OperatingSystem.IsWindows();
        bool IsSafe(char c) =>
            char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '=' or '@' or '+' or ',' || (windows && c == '\\');

        if (value.Length > 0 && value.All(IsSafe))
        {
            return value;
        }

        if (!windows)
        {
            return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }

        if (value.Contains('$', StringComparison.Ordinal) || value.Contains('`', StringComparison.Ordinal))
        {
            // Literal in PowerShell, where those two characters are special.
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        // A trailing backslash would escape the closing quote.
        var escaped = value.Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + (escaped.EndsWith('\\') ? "\\" : string.Empty) + "\"";
    }
}
