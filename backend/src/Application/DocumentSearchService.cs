using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>Phase 5 search: free-text match over file name, vendor and OCR text
/// plus filters (category, vendor, extracted-date range, tag, archive, review
/// queue, status), newest first. Members only — the repository scopes every
/// query to the workspace, so a search can never leak another workspace's
/// documents. Searches are not audited (a read that would flood the log).</summary>
public class DocumentSearchService
{
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceAccessService _access;

    /// <summary>Categories the classifier suggests. Must match
    /// <c>DocumentClassifier</c>'s list exactly.</summary>
    public static readonly string[] KnownCategories =
        ["Receipt", "Invoice", "Contract", "BankStatement", "Letter", "Other"];

    public DocumentSearchService(IDocumentRepository documents, IWorkspaceAccessService access)
    {
        _documents = documents; _access = access;
    }

    /// <summary>Runs a search. At least one criterion is required — an empty
    /// search would just be the document list. 400 on an unknown category or
    /// status, or on a reversed date range.</summary>
    public async Task<List<SearchResultDto>> SearchAsync(
        string userId, Guid workspaceId, DocumentSearchCriteria criteria, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var hasCriteria = !string.IsNullOrWhiteSpace(criteria.Query)
            || !string.IsNullOrWhiteSpace(criteria.Category)
            || !string.IsNullOrWhiteSpace(criteria.Vendor)
            || criteria.From.HasValue || criteria.To.HasValue
            || criteria.TagId.HasValue || criteria.ArchiveId.HasValue
            || criteria.NeedsReview.HasValue || criteria.Status.HasValue;
        if (!hasCriteria)
            throw new ValidationException("Give the search something to look for: text or at least one filter.");
        if (criteria.Category is not null && !KnownCategories.Contains(criteria.Category, StringComparer.Ordinal))
            throw new ValidationException($"Unknown category '{criteria.Category}'.");
        if (criteria.From.HasValue && criteria.To.HasValue && criteria.From.Value.Date > criteria.To.Value.Date)
            throw new ValidationException("The 'from' date must not be after the 'to' date.");

        var docs = await _documents.SearchAsync(workspaceId, criteria, ct);
        return docs.Select(d => new SearchResultDto(
            DocumentDtos.ToDto(d), BuildSnippet(d, criteria.Query), MatchedField(d, criteria.Query))).ToList();
    }

    /// <summary>Which field the free text matched in, for the snippet label.
    /// "ocr" wins over "vendor" over "fileName". With no query (filters only)
    /// the snippet comes from the OCR text when there is one.</summary>
    private static string MatchedField(Document d, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return d.OcrText is null ? "fileName" : "ocr";
        var terms = QueryTerms(query);
        if (terms.Any(t => ContainsTerm(d.OcrText, t))) return "ocr";
        if (terms.Any(t => ContainsTerm(d.ExtractedVendor, t))) return "vendor";
        return "fileName";
    }

    /// <summary>Short excerpt around the first match: 60 characters before it,
    /// the matched term, 80 after — with "…" where the text was cut. Falls back
    /// to the start of the OCR text (filters-only search) or the file name.</summary>
    private static string BuildSnippet(Document d, string? query)
    {
        const int before = 60, after = 80;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = QueryTerms(query);
            var source = FirstMatchSource(d, terms, out var term, out var index);
            if (source is not null && term is not null)
            {
                var start = Math.Max(0, index - before);
                var end = Math.Min(source.Length, index + term.Length + after);
                var excerpt = source[start..end];
                if (start > 0) excerpt = "…" + excerpt;
                if (end < source.Length) excerpt += "…";
                return excerpt;
            }
        }
        if (!string.IsNullOrWhiteSpace(d.OcrText))
        {
            var text = SingleLine(d.OcrText);
            return text.Length <= before + after ? text : text[..(before + after)] + "…";
        }
        return d.OriginalFileName;
    }

    /// <summary>The text the query matched in (OCR text, vendor, file name, in
    /// that order), plus the matched term and its position. Null when nothing
    /// matched (cannot happen for a text search — the row matched something —
    /// but the terms may differ by stemming on Postgres: "invoices" matches
    /// "invoice", which the literal term search here would miss).</summary>
    private static string? FirstMatchSource(Document d, string[] terms, out string? term, out int index)
    {
        foreach (var source in new[] { d.OcrText, d.ExtractedVendor, d.OriginalFileName })
        {
            if (string.IsNullOrEmpty(source)) continue;
            foreach (var t in terms)
            {
                var i = source.IndexOf(t, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { term = t; index = i; return source; }
            }
        }
        term = null; index = -1;
        return null;
    }

    private static string[] QueryTerms(string query)
        => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool ContainsTerm(string? source, string term)
        => source is not null && source.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>OCR text is multi-line; a snippet must read as one line.</summary>
    private static string SingleLine(string text)
        => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
