using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using DotnetDiag.Core.Hints;
using DotnetDiag.Core.Models;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.ViewModels;

public sealed record KnownErrorRow(
    string Code,
    string Title,
    DiagnosticCategory Category,
    string CategoryName,
    Tone Tone,
    string Explanation,
    IReadOnlyList<string> Fixes,
    string DocsUrl)
{
    /// <summary>Plain text for the clipboard, e.g. to paste into a chat or an issue.</summary>
    public string CopyText =>
        $"{Code}: {Title} ({CategoryName})\n\n{Explanation}\n\nFixes:\n{string.Join('\n', Fixes.Select(f => "- " + f))}\n\nDocs: {DocsUrl}";
}

/// <summary>The Explain page: browse and search the offline catalog (<c>dotnet-diag explain</c>).</summary>
public sealed partial class ExplainViewModel : ObservableObject
{
    private static readonly IReadOnlyList<KnownErrorRow> Catalog =
    [
        .. KnownErrorCatalog.All.OrderBy(e => e.Code, StringComparer.Ordinal).Select(entry =>
        {
            var category = DiagnosticCategories.FromCode(entry.Code);
            return new KnownErrorRow(entry.Code, entry.Title, category, category.DisplayName(), Tones.For(category), entry.Explanation, entry.Fixes, entry.DocsUrl);
        }),
    ];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private Choice<DiagnosticCategory?> _category;

    [ObservableProperty]
    private KnownErrorRow? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnknownText))]
    private string? _unknownCode;

    [ObservableProperty]
    private string? _unknownDocsUrl;

    [ObservableProperty]
    private string _countText = string.Empty;

    public ExplainViewModel()
    {
        Categories =
        [
            new($"All ({Catalog.Count})", null),
            .. Catalog.GroupBy(row => row.Category).Select(g => new Choice<DiagnosticCategory?>($"{g.First().CategoryName} ({g.Count()})", g.Key)),
        ];
        _category = Categories[0];
        Refresh();
    }

    public IReadOnlyList<Choice<DiagnosticCategory?>> Categories { get; }

    /// <summary>The catalog entries that match the search box and the category.</summary>
    public ObservableCollection<KnownErrorRow> Entries { get; } = [];

    public string? UnknownText => UnknownCode is null ? null : $"{UnknownCode} is not in the offline catalog.";

    /// <summary>Jumps straight to one code, e.g. from a diagnostic in a report.</summary>
    public void Show(string code)
    {
        Category = Categories[0];
        Query = code.Trim();
    }

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnCategoryChanged(Choice<DiagnosticCategory?> value) => Refresh();

    private void Refresh()
    {
        var query = Query.Trim();
        var matches = Catalog
            .Where(row => Category.Value is null || row.Category == Category.Value)
            .Where(row => query.Length == 0
                          || row.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || row.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || row.Explanation.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assigning the selection first keeps the list control from briefly selecting nothing.
        var previous = Selected;
        Entries.Clear();
        foreach (var row in matches)
        {
            Entries.Add(row);
        }

        Selected = matches.FirstOrDefault(row => row.Code.Equals(query, StringComparison.OrdinalIgnoreCase))
                   ?? (previous is not null && matches.Contains(previous) ? previous : matches.FirstOrDefault());

        CountText = matches.Count == Catalog.Count ? $"{Catalog.Count} codes" : $"{matches.Count} of {Catalog.Count} codes";

        // Same answer as `dotnet-diag explain <code>` for a code the catalog does not have: point at the docs.
        var isUnknown = DiagnosticCode().IsMatch(query) && KnownErrorCatalog.Find(query) is null;
        UnknownCode = isUnknown ? query.ToUpperInvariant() : null;
        UnknownDocsUrl = isUnknown ? KnownErrorCatalog.DocsUrlFor(query) : null;
    }

    [GeneratedRegex(@"^[A-Za-z]{2,8}\d{2,6}$")]
    private static partial Regex DiagnosticCode();
}
