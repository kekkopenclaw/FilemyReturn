using System.Net;
using System.Net.Http.Json;
using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>
/// Pipeline integration tests: the whole upload → OCR → classify → search flow
/// against the real API, the human-review queue (list filter + confirm-category),
/// duplicate rules across workspaces, search scoping/casing, and the OCR claim
/// (a document is processed exactly once). Tests that need the tesseract binary
/// skip their OCR-text assertions when it is missing, exactly like ReceiptOcrTests.
/// </summary>
public class PipelineIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PipelineIntegrationTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private static bool TesseractAvailable()
        => File.Exists("/usr/bin/tesseract") || File.Exists("/usr/local/bin/tesseract");

    private static string TestData(string name)
        => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && name.Contains("invoice")
            ? InvoiceFixtures.EnsureTestData(name)
            : DemoReceiptFixtures.EnsureTestData(name);

    // ---------- full pipeline: receipt ----------

    [Fact]
    public async Task Pipeline_Receipt_EndToEnd_ClassificationFields_Searchable()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("pipe-receipt");
        var docId = await SeedFileAsync(workspaceId, userId, "demo-receipt.png", "image/png");

        using (var scope = _factory.Services.CreateScope())
        {
            var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
            Assert.True(await processing.ProcessAsync(workspaceId, docId, userId));
        }

        var detail = await GetDetailAsync(client, workspaceId, docId);
        Assert.Equal("Ready", detail.Status);
        Assert.Equal("Completed", detail.OcrStatus);
        Assert.Equal("Receipt", detail.SuggestedCategory);
        Assert.False(detail.NeedsReview);
        Assert.NotNull(detail.ClassificationConfidence);
        Assert.True(detail.ClassificationConfidence.Value >= 60, $"confidence was {detail.ClassificationConfidence}");
        Assert.Contains("Receipt", detail.ClassificationReason ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Null(detail.ConfirmedCategory);

        // Search by vendor and by amount in the OCR text; unrelated term finds nothing.
        var byVendor = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?search=corner%20cafe");
        Assert.NotNull(byVendor);
        Assert.Single(byVendor);
        var byUnrelated = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?search=plumber");
        Assert.NotNull(byUnrelated);
        Assert.Empty(byUnrelated);

        if (!TesseractAvailable()) return;
        var byOcrText = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?search=flat%20white");
        Assert.NotNull(byOcrText);
        Assert.Single(byOcrText);
        Assert.Equal(docId, byOcrText[0].Id);
    }

    // ---------- full pipeline: invoice ----------

    [Fact]
    public async Task Pipeline_Invoice_EndToEnd_ClassifiesInvoice()
    {
        if (!TesseractAvailable()) return; // needs the real OCR binary.
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("pipe-invoice");
        var docId = await SeedFileAsync(workspaceId, userId, "demo-invoice.png", "image/png");

        using (var scope = _factory.Services.CreateScope())
        {
            var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
            Assert.True(await processing.ProcessAsync(workspaceId, docId, userId));
        }

        var detail = await GetDetailAsync(client, workspaceId, docId);
        Assert.Equal("Ready", detail.Status);
        Assert.Equal("Invoice", detail.SuggestedCategory);
        Assert.False(detail.NeedsReview);
        Assert.Equal("ACME SUPPLIES LTD", detail.ExtractedVendor);
        Assert.Equal(new DateTime(2026, 11, 12), detail.ExtractedDate);
        Assert.Equal(128.50m, detail.ExtractedTotal);
        Assert.Equal("£", detail.ExtractedCurrency);
    }

    // ---------- claim: processed exactly once ----------

    [Fact]
    public async Task Process_AlreadyProcessed_ReturnsFalse()
    {
        var (_, workspaceId, userId) = await SetupWorkspaceAsync("pipe-claim");
        var docId = await SeedFileAsync(workspaceId, userId, "demo-receipt.png", "image/png");

        using var scope = _factory.Services.CreateScope();
        var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
        Assert.True(await processing.ProcessAsync(workspaceId, docId, userId));
        // Second attempt: the document is no longer Queued, so the claim fails.
        Assert.False(await processing.ProcessAsync(workspaceId, docId, userId));
    }

    // ---------- human-review queue + confirm-category ----------

    [Fact]
    public async Task ReviewQueue_ListFilter_AndConfirmCategory()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("pipe-review");
        var reviewId = await SeedReviewDocumentAsync(workspaceId, userId);
        var readyId = await SeedFileAsync(workspaceId, userId, "demo-receipt.png", "image/png");

        var reviewList = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?needsReview=true");
        Assert.NotNull(reviewList);
        Assert.Contains(reviewList, d => d.Id == reviewId);
        Assert.DoesNotContain(reviewList, d => d.Id == readyId);

        var clearList = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?needsReview=false");
        Assert.NotNull(clearList);
        Assert.DoesNotContain(clearList, d => d.Id == reviewId);
        Assert.Contains(clearList, d => d.Id == readyId);

        var confirm = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/documents/{reviewId}/confirm-category", new { Category = "Invoice" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmed = await confirm.Content.ReadFromJsonAsync<DocDetail>() ?? throw new InvalidOperationException("Empty confirm response.");
        Assert.Equal("Invoice", confirmed.ConfirmedCategory);
        Assert.Equal("Invoice", confirmed.SuggestedCategory);
        Assert.False(confirmed.NeedsReview);

        var reviewListAfter = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?needsReview=true");
        Assert.NotNull(reviewListAfter);
        Assert.DoesNotContain(reviewListAfter, d => d.Id == reviewId);
    }

    [Fact]
    public async Task ConfirmCategory_UnknownCategory_Returns400_ReadOnly_Returns403_NonMember_Returns404()
    {
        var (ownerClient, workspaceId, userId) = await SetupWorkspaceAsync("pipe-confirm");
        var reviewId = await SeedReviewDocumentAsync(workspaceId, userId);

        var unknown = await ownerClient.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/documents/{reviewId}/confirm-category", new { Category = "TaxReturn" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var readerClient = _factory.CreateClient();
        var reader = await TestHelpers.RegisterAndLoginAsync(readerClient, TestHelpers.UniqueEmail("pipe-reader"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, reader.Email, "ReadOnly");
        TestHelpers.Authed(readerClient, reader.AccessToken);
        var readOnly = await readerClient.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/documents/{reviewId}/confirm-category", new { Category = "Invoice" });
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);

        var strangerClient = _factory.CreateClient();
        var stranger = await TestHelpers.RegisterAndLoginAsync(strangerClient, TestHelpers.UniqueEmail("pipe-stranger"));
        TestHelpers.Authed(strangerClient, stranger.AccessToken);
        var strangerCall = await strangerClient.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/documents/{reviewId}/confirm-category", new { Category = "Invoice" });
        Assert.Equal(HttpStatusCode.NotFound, strangerCall.StatusCode);
    }

    // ---------- duplicates are per workspace ----------

    [Fact]
    public async Task Duplicate_SameFileInDifferentWorkspace_Allowed()
    {
        var (clientA, workspaceA, _) = await SetupWorkspaceAsync("pipe-dup-a");
        var (clientB, workspaceB, _) = await SetupWorkspaceAsync("pipe-dup-b");
        var bytes = PdfBytes(200);

        var first = await clientA.PostAsync($"/api/workspaces/{workspaceA}/documents", PdfContent(bytes));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // Same bytes, different workspace: allowed (the duplicate rule is per workspace).
        var second = await clientB.PostAsync($"/api/workspaces/{workspaceB}/documents", PdfContent(bytes, "copy.pdf"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstDoc = await first.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
        var secondDoc = await second.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
        Assert.NotEqual(firstDoc.Id, secondDoc.Id);
        Assert.Equal(firstDoc.Sha256Hash, secondDoc.Sha256Hash);
    }

    // ---------- search: case-insensitive and workspace-scoped ----------

    [Fact]
    public async Task Search_IsCaseInsensitive_AndWorkspaceScoped()
    {
        var (clientA, workspaceA, userId) = await SetupWorkspaceAsync("pipe-search-a");
        var docId = await SeedFileAsync(workspaceA, userId, "demo-receipt.png", "image/png");
        using (var scope = _factory.Services.CreateScope())
        {
            var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
            Assert.True(await processing.ProcessAsync(workspaceA, docId, userId));
        }

        // Uppercase search term still matches (SQLite LIKE is case-insensitive for ASCII).
        var upper = await clientA.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceA}/documents?search=FLAT%20WHITE");
        if (TesseractAvailable())
        {
            Assert.NotNull(upper);
            Assert.Single(upper);
            Assert.Equal(docId, upper[0].Id);
        }

        // The same search in another workspace finds nothing — search never leaks.
        var (clientB, workspaceB, _) = await SetupWorkspaceAsync("pipe-search-b");
        var leaked = await clientB.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceB}/documents?search=flat%20white");
        Assert.NotNull(leaked);
        Assert.Empty(leaked);
        var leakedByName = await clientB.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceB}/documents?search=demo-receipt");
        Assert.NotNull(leakedByName);
        Assert.Empty(leakedByName);
    }

    // ---------- audit: processing is logged ----------

    [Fact]
    public async Task Audit_ProcessingLogsCompletion()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("pipe-audit");
        var docId = await SeedFileAsync(workspaceId, userId, "demo-receipt.png", "image/png");
        using (var scope = _factory.Services.CreateScope())
        {
            var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
            Assert.True(await processing.ProcessAsync(workspaceId, docId, userId));
        }

        var logs = await client.GetAsync($"/api/workspaces/{workspaceId}/audit-logs");
        logs.EnsureSuccessStatusCode();
        var entries = await logs.Content.ReadFromJsonAsync<List<AuditBody>>() ?? [];
        Assert.Contains(entries, e => e.Action == "Document.OcrCompleted");
    }

    // ---------- helpers ----------

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private async Task<Guid> SeedFileAsync(Guid workspaceId, string userId, string fileName, string mimeType)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bytes = await File.ReadAllBytesAsync(TestData(fileName));
        string key;
        await using (var stream = new MemoryStream(bytes))
            key = await storage.SaveAsync(stream, fileName, mimeType);
        var doc = new Document
        {
            WorkspaceId = workspaceId,
            OriginalFileName = fileName,
            StorageKey = key,
            Sha256Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            FileSizeBytes = bytes.Length,
            MimeType = mimeType,
            Status = DocumentStatus.Uploaded,
            OcrStatus = OcrStatus.Queued,
            CreatedById = userId
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>Seeds a Ready document that needs human review (no category signals matched).</summary>
    private async Task<Guid> SeedReviewDocumentAsync(Guid workspaceId, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = new Document
        {
            WorkspaceId = workspaceId,
            OriginalFileName = "mystery-scan.png",
            StorageKey = $"{Guid.NewGuid():N}.png",
            Sha256Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())),
            FileSizeBytes = 1234,
            MimeType = "image/png",
            Status = DocumentStatus.Ready,
            OcrStatus = OcrStatus.Completed,
            OcrText = "xkq zztop 12345 !!! qwerty asdf",
            OcrConfidence = 55,
            SuggestedCategory = "Other",
            ClassificationConfidence = 30,
            ClassificationReason = "No category signals matched the text.",
            NeedsReview = true,
            CreatedById = userId
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private async Task<DocDetail> GetDetailAsync(HttpClient client, Guid workspaceId, Guid docId)
    {
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocDetail>() ?? throw new InvalidOperationException("Empty document response.");
    }

    private static byte[] PdfBytes(int totalLength)
    {
        var header = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n"u8.ToArray();
        var bytes = new byte[totalLength];
        Array.Copy(header, bytes, Math.Min(header.Length, totalLength));
        for (var i = header.Length; i < totalLength; i++) bytes[i] = (byte)'A';
        return bytes;
    }

    private static MultipartFormDataContent PdfContent(byte[] bytes, string fileName = "test.pdf")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        return content;
    }

    private record DocBody(Guid Id, string Sha256Hash);
    private record DocDetail(Guid Id, string Status, string OcrStatus, string? OcrText, double? OcrConfidence,
        string? ExtractedVendor, DateTime? ExtractedDate, decimal? ExtractedTotal, string? ExtractedCurrency,
        string? SuggestedCategory, double? ClassificationConfidence, string? ClassificationReason,
        bool NeedsReview, string? ConfirmedCategory);
    private record AuditBody(Guid Id, string Action);
}
