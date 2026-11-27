using System.Net;
using System.Net.Http.Json;
using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>Phase 5 search: free-text match over file name, vendor and OCR text
/// plus filters (category, vendor, date range, tag, archive, review queue,
/// status), with a snippet around the match. Workspace-scoped like everything
/// else. The Postgres full-text path is proven by SQL translation
/// (ToQueryString) because the test suite runs on SQLite.</summary>
public class SearchTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SearchTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private record SearchHit(SearchDoc Document, string Snippet, string MatchedField);
    private record SearchDoc(Guid Id, string OriginalFileName);

    private static async Task<List<SearchHit>> SearchAsync(HttpClient client, Guid workspaceId, string query)
    {
        var res = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/search{query}");
        res.EnsureSuccessStatusCode();
        var hits = await res.Content.ReadFromJsonAsync<List<SearchHit>>() ?? throw new InvalidOperationException("Empty search response.");
        return hits;
    }

    /// <summary>Seeds a document that already looks OCR-processed, then fills in
    /// the fields the seed helper does not take (OCR text, date, review flag).</summary>
    private async Task<Guid> SeedSearchableDocAsync(Guid workspaceId, string userId,
        string fileName, string? vendor, string? category,
        string? ocrText = null, DateTime? date = null, bool needsReview = false)
    {
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId, fileName, vendor, category);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.FindAsync(docId) ?? throw new InvalidOperationException("Seeded document missing.");
        if (ocrText is not null) doc.OcrText = ocrText;
        if (date.HasValue) doc.ExtractedDate = date.Value;
        doc.NeedsReview = needsReview;
        await db.SaveChangesAsync();
        return docId;
    }

    [Fact]
    public async Task Search_ByOcrTerm_ReturnsMatch_WithSnippetAroundTheTerm()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-ocr");
        await SeedSearchableDocAsync(workspaceId, userId, "receipt.png", "Corner Cafe", "Receipt",
            ocrText: "THE CORNER CAFE\nflat white 3.20\ncroissant 2.10\nTOTAL 5.30");

        var hits = await SearchAsync(client, workspaceId, "?q=flat%20white");

        Assert.Single(hits);
        Assert.Contains("flat white", hits[0].Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ocr", hits[0].MatchedField);
    }

    [Fact]
    public async Task Search_ByVendorSubstring_IsCaseInsensitive()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-vendor");
        await SeedSearchableDocAsync(workspaceId, userId, "receipt.png", "Tesco Extra", "Receipt",
            ocrText: "TOTAL 47.82");

        var hits = await SearchAsync(client, workspaceId, "?q=TESCO");

        Assert.Single(hits);
        Assert.Equal("vendor", hits[0].MatchedField);
    }

    [Fact]
    public async Task Search_CategoryFilter_UsesEffectiveCategory_ConfirmedWins()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-cat");
        var docId = await SeedSearchableDocAsync(workspaceId, userId, "invoice.pdf", "Plumber", "Receipt",
            ocrText: "INVOICE 123");
        // A human corrected the suggestion to Invoice.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await db.Documents.FindAsync(docId) ?? throw new InvalidOperationException("Seeded document missing.");
            doc.ConfirmedCategory = "Invoice";
            await db.SaveChangesAsync();
        }

        var asInvoice = await SearchAsync(client, workspaceId, "?category=Invoice");
        var asReceipt = await SearchAsync(client, workspaceId, "?category=Receipt");

        Assert.Single(asInvoice);
        Assert.Empty(asReceipt);
    }

    [Fact]
    public async Task Search_DateRangeFilter_IsInclusiveOnBothEnds()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-date");
        await SeedSearchableDocAsync(workspaceId, userId, "nov.png", "Shop", "Receipt",
            date: new DateTime(2026, 11, 24));
        await SeedSearchableDocAsync(workspaceId, userId, "oct.png", "Shop", "Receipt",
            date: new DateTime(2026, 10, 5));

        var hits = await SearchAsync(client, workspaceId, "?from=2026-11-01&to=2026-11-30");

        Assert.Single(hits);
        Assert.Equal("nov.png", hits[0].Document.OriginalFileName);
    }

    [Fact]
    public async Task Search_TagFilter_ReturnsOnlyTaggedDocuments()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-tag");
        var taggedId = await SeedSearchableDocAsync(workspaceId, userId, "tagged.png", "Shop", "Receipt");
        await SeedSearchableDocAsync(workspaceId, userId, "plain.png", "Shop", "Receipt");

        var tagRes = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { name = "Tax 2026", colour = "#0e7c66" });
        tagRes.EnsureSuccessStatusCode();
        var tag = await tagRes.Content.ReadFromJsonAsync<TagBody>() ?? throw new InvalidOperationException("Empty tag response.");
        var assign = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{taggedId}/tags", new { tagId = tag.Id });
        assign.EnsureSuccessStatusCode();

        var hits = await SearchAsync(client, workspaceId, $"?tagId={tag.Id}");

        Assert.Single(hits);
        Assert.Equal("tagged.png", hits[0].Document.OriginalFileName);
    }

    private record TagBody(Guid Id);

    [Fact]
    public async Task Search_NeedsReviewFilter_ReturnsOnlyTheReviewQueue()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-review");
        await SeedSearchableDocAsync(workspaceId, userId, "review.png", "Shop", "Receipt", needsReview: true);
        await SeedSearchableDocAsync(workspaceId, userId, "fine.png", "Shop", "Receipt", needsReview: false);

        var hits = await SearchAsync(client, workspaceId, "?needsReview=true");

        Assert.Single(hits);
        Assert.Equal("review.png", hits[0].Document.OriginalFileName);
    }

    [Fact]
    public async Task Search_FiltersOnly_ReturnsSnippetFromOcrStart()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("sr-filters");
        await SeedSearchableDocAsync(workspaceId, userId, "receipt.png", "Corner Cafe", "Receipt",
            ocrText: "THE CORNER CAFE\nflat white 3.20\nTOTAL 5.30");

        var hits = await SearchAsync(client, workspaceId, "?category=Receipt");

        Assert.Single(hits);
        Assert.Equal("ocr", hits[0].MatchedField);
        Assert.StartsWith("THE CORNER CAFE", hits[0].Snippet);
    }

    [Fact]
    public async Task Search_NoCriteria_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("sr-empty");
        var res = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/search");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Search_UnknownCategory_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("sr-badcat");
        var res = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/search?category=Banana");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Search_ReversedDateRange_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("sr-baddate");
        var res = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/search?from=2026-12-01&to=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Search_IsolatedToWorkspace()
    {
        var (clientA, workspaceA, userA) = await SetupWorkspaceAsync("sr-iso-a");
        var (clientB, workspaceB, _) = await SetupWorkspaceAsync("sr-iso-b");
        await SeedSearchableDocAsync(workspaceA, userA, "secret.png", "Shop", "Receipt",
            ocrText: "flat white 3.20");

        var hits = await SearchAsync(clientB, workspaceB, "?q=flat%20white");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/documents/search?q=test");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    /// <summary>The Postgres full-text path never executes in this suite (the
    /// factory is SQLite), so prove the query translates instead: the generated
    /// SQL must use to_tsvector / plainto_tsquery, with no database connection.</summary>
    [Fact]
    public void PostgresTextSearch_TranslatesToTsVectorQuery()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=docuarchive;Username=docuarchive;Password=test")
            .Options;
        using var db = new AppDbContext(options);
        var sql = EfDocumentRepository.ApplyTextSearch(db.Documents, "flat white", useFullTextSearch: true).ToQueryString();
        Assert.Contains("to_tsvector", sql);
        Assert.Contains("plainto_tsquery", sql);
    }

    [Fact]
    public void SqliteTextSearch_TranslatesToLikeQuery()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AppDbContext(options);
        var sql = EfDocumentRepository.ApplyTextSearch(db.Documents, "flat white", useFullTextSearch: false).ToQueryString();
        Assert.Contains("LIKE", sql);
        Assert.DoesNotContain("to_tsvector", sql);
    }
}
