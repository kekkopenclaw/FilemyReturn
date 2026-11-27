using System.Net;
using System.Net.Http.Json;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>Phase 4 metadata editing: full-form PUT of a document's extracted
/// fields (file name, vendor, date, total, category), validation (file name,
/// date range, total range, known category), role gating, workspace isolation,
/// category confirm/clear semantics, and audit (only real changes logged).</summary>
public class MetadataEditingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public MetadataEditingTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private record DocBody(Guid Id, string OriginalFileName, string? ExtractedVendor, DateTime? ExtractedDate,
        decimal? ExtractedTotal, string? SuggestedCategory, string? ConfirmedCategory, bool NeedsReview);

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private static object MetadataJson(string fileName, string? vendor = null, string? date = null,
        decimal? total = null, string? category = null)
        => new
        {
            OriginalFileName = fileName, ExtractedVendor = vendor,
            ExtractedDate = date, ExtractedTotal = total, Category = category
        };

    private async Task<DocBody> PutMetadataAsync(HttpClient client, Guid workspaceId, Guid docId, object body)
    {
        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty metadata response.");
    }

    private async Task SetDocFieldsAsync(Guid docId, DateTime? date, decimal? total, bool needsReview = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.FirstAsync(d => d.Id == docId);
        doc.ExtractedDate = date;
        doc.ExtractedTotal = total;
        doc.NeedsReview = needsReview;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Update_AllFields_Returns200_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-all");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "tesco-receipt.png", "Tesco Extra", "Receipt");

        var updated = await PutMetadataAsync(client, workspaceId, docId,
            MetadataJson("weekly-shop.png", "Sainsbury's", "2026-11-25", 50.00m, "Invoice"));

        Assert.Equal("weekly-shop.png", updated.OriginalFileName);
        Assert.Equal("Sainsbury's", updated.ExtractedVendor);
        Assert.Equal(new DateTime(2026, 11, 25), updated.ExtractedDate);
        Assert.Equal(50.00m, updated.ExtractedTotal);
        Assert.Equal("Invoice", updated.ConfirmedCategory);
        Assert.Equal("Invoice", updated.SuggestedCategory);
        Assert.False(updated.NeedsReview);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.MetadataUpdated"));
    }

    [Fact]
    public async Task Clear_VendorDateTotal_SetsNull()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-clear");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "receipt.png", "Tesco", "Receipt");
        await SetDocFieldsAsync(docId, new DateTime(2026, 11, 24), 47.82m);

        var updated = await PutMetadataAsync(client, workspaceId, docId,
            MetadataJson("receipt.png", null, null, null, null));

        Assert.Null(updated.ExtractedVendor);
        Assert.Null(updated.ExtractedDate);
        Assert.Null(updated.ExtractedTotal);
        // Clearing the category leaves the suggestion in place.
        Assert.Null(updated.ConfirmedCategory);
        Assert.Equal("Receipt", updated.SuggestedCategory);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.MetadataUpdated"));
    }

    [Fact]
    public async Task UnknownCategory_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("meta-badcat");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "u1");

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("test.pdf", category: "Groceries"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FutureDate_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("meta-future");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "u1");
        var future = DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd");

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("test.pdf", date: future));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DateBefore1900_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("meta-old");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "u1");

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("test.pdf", date: "1899-12-31"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NegativeTotal_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("meta-neg");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "u1");

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("test.pdf", total: -1.00m));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TotalTooLarge_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("meta-big");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "u1");

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("test.pdf", total: 1_000_000_000m));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BadFileName_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("meta-badname");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "u1");

        // Empty name fails model validation; a path separator and ".." fail the service check.
        var empty = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson(""));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var slash = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("folder/receipt.png"));
        Assert.Equal(HttpStatusCode.BadRequest, slash.StatusCode);

        var dotdot = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson(".."));
        Assert.Equal(HttpStatusCode.BadRequest, dotdot.StatusCode);

        var tooLong = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson(new string('a', 256) + ".png"));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task AsReadOnly_Returns403()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-ro");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var reader = _factory.CreateClient();
        var readerSession = await TestHelpers.RegisterAndLoginAsync(reader, TestHelpers.UniqueEmail("meta-ro-reader"));
        await TestHelpers.AddMemberAsync(client, workspaceId, readerSession.Email, "ReadOnly");
        TestHelpers.Authed(reader, readerSession.AccessToken);

        var response = await reader.PutAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/metadata",
            MetadataJson("renamed.pdf", "Tesco"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OtherWorkspace_Returns404()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-xws");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var (otherClient, otherWorkspaceId, _) = await SetupWorkspaceAsync("meta-xws-other");

        var response = await otherClient.PutAsJsonAsync($"/api/workspaces/{otherWorkspaceId}/documents/{docId}/metadata",
            MetadataJson("renamed.pdf", "Tesco"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetCategory_Confirms_AndClearsNeedsReview()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-confirm");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "invoice.pdf", "Supplier Ltd", "Receipt");
        await SetDocFieldsAsync(docId, null, null, needsReview: true);

        var updated = await PutMetadataAsync(client, workspaceId, docId,
            MetadataJson("invoice.pdf", "Supplier Ltd", null, null, "Invoice"));

        Assert.Equal("Invoice", updated.ConfirmedCategory);
        Assert.Equal("Invoice", updated.SuggestedCategory);
        Assert.False(updated.NeedsReview);
    }

    [Fact]
    public async Task ClearCategory_ClearsConfirmed_LeavesSuggestion()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-clearcat");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "invoice.pdf", "Supplier Ltd", "Receipt");
        // Confirm a category first, then clear it.
        await PutMetadataAsync(client, workspaceId, docId,
            MetadataJson("invoice.pdf", "Supplier Ltd", null, null, "Invoice"));

        var updated = await PutMetadataAsync(client, workspaceId, docId,
            MetadataJson("invoice.pdf", "Supplier Ltd", null, null, null));

        Assert.Null(updated.ConfirmedCategory);
        Assert.Equal("Invoice", updated.SuggestedCategory);
    }

    [Fact]
    public async Task NoChange_Returns200_AndIsNotAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("meta-noop");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "receipt.png", "Tesco", "Receipt");

        // Same file name and vendor, nothing cleared, no category given:
        // confirming nothing and changing nothing must not write an audit entry.
        var updated = await PutMetadataAsync(client, workspaceId, docId,
            MetadataJson("receipt.png", "Tesco", null, null, null));

        Assert.Equal("receipt.png", updated.OriginalFileName);
        Assert.Equal(0, TestHelpers.CountAudit(_factory, userId, "Document.MetadataUpdated"));
    }
}
