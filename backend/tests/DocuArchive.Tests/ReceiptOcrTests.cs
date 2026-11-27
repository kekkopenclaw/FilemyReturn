using System.Net;
using System.Net.Http.Json;
using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using DocuArchive.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocuArchive.Tests;

/// <summary>
/// Phase 3 receipt-OCR tests. The demo receipt is fictional (THE CORNER CAFE, generated
/// with Pillow — see docs/demo-receipt/) so no real personal data is ever used.
/// The engine tests need the `tesseract` binary: it is installed on GitHub ubuntu runners
/// and in the Docker image (apt-get install tesseract-ocr poppler-utils). When the binary
/// is missing the engine returns Success=false instead of fake text, and the engine test
/// asserts exactly that instead of passing.
/// </summary>
public class ReceiptOcrTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ReceiptOcrTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private static string TestData(string name)
    {
        var dir = AppContext.BaseDirectory;
        var path = Path.Combine(dir, "TestData", name);
        if (File.Exists(path)) return path;
        // Fallback: repo-relative (dotnet test from the test project dir).
        return Path.Combine(Directory.GetCurrentDirectory(), "TestData", name);
    }

    // ---------- ReceiptFieldExtractor (pure, no I/O) ----------

    [Fact]
    public void Extract_DemoReceiptText_ReturnsVendorDateTotalAndReceiptCategory()
    {
        const string text = """
            THE CORNER CAFE
            12 Market Street, London
            Tel: 020 7946 0123
            24 Nov 2026  10:42   Till 2
            Flat White              3.40
            Croissant               2.85
            Orange Juice            3.10
            Toasted Sandwich        5.98
            Subtotal               15.33
            VAT 20%                 3.07
            TOTAL                 £18.40
            Card **** 4242
            Thank you - see you soon!
            """;
        var fields = ReceiptFieldExtractor.Extract(text);
        Assert.Equal("THE CORNER CAFE", fields.Vendor);
        Assert.Equal(new DateTime(2026, 11, 24), fields.Date);
        Assert.Equal(18.40m, fields.Total);
        Assert.Equal("£", fields.Currency);
        Assert.Equal("Receipt", fields.SuggestedCategory);
    }

    [Fact]
    public void Extract_InvoiceLikeText_WithoutTotalLine_FallsBackToLargestAmount()
    {
        const string text = "ACME SUPPLIES LTD\nInvoice 1042\n12/11/2026\nWidgets 120.00\nDelivery 8.50\n";
        var fields = ReceiptFieldExtractor.Extract(text);
        Assert.Equal("ACME SUPPLIES LTD", fields.Vendor);
        Assert.Equal(new DateTime(2026, 11, 12), fields.Date);
        Assert.Equal(120.00m, fields.Total);
        Assert.Null(fields.SuggestedCategory); // no receipt keywords and no TOTAL line
    }

    [Fact]
    public void Extract_EmptyText_ReturnsAllNull()
    {
        var fields = ReceiptFieldExtractor.Extract("   \n\n  ");
        Assert.Null(fields.Vendor);
        Assert.Null(fields.Date);
        Assert.Null(fields.Total);
        Assert.Null(fields.SuggestedCategory);
    }

    [Fact]
    public void Extract_TotalLineWithDollarSign_KeepsCurrency()
    {
        const string text = "CORNER SHOP\n01 Jan 2026\nTOTAL $42.10\n";
        var fields = ReceiptFieldExtractor.Extract(text);
        Assert.Equal(42.10m, fields.Total);
        Assert.Equal("$", fields.Currency);
        Assert.Equal("Receipt", fields.SuggestedCategory);
    }

    // ---------- Tesseract engine (real binary) ----------

    [Fact]
    public async Task Engine_DemoReceiptPng_ExtractsVendorDateTotal()
    {
        var engine = MakeEngine();
        var key = await StoreTestFileAsync("demo-receipt.png", "image/png");
        var result = await engine.ExtractTextAsync(key);
        if (!TesseractAvailable())
        {
            Assert.False(result.Success);
            Assert.Null(result.Text);
            return; // binary missing: honest failure, not fake text.
        }
        Assert.True(result.Success);
        Assert.NotNull(result.Text);
        Assert.Contains("CORNER CAFE", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("18.40", result.Text);
        Assert.NotNull(result.Confidence);
        Assert.InRange(result.Confidence.Value, 50, 100);

        var fields = ReceiptFieldExtractor.Extract(result.Text);
        Assert.Equal("THE CORNER CAFE", fields.Vendor);
        Assert.Equal(18.40m, fields.Total);
        Assert.Equal("Receipt", fields.SuggestedCategory);
    }

    [Fact]
    public async Task Engine_DemoReceiptPdf_ScannedImagePdf_ExtractsText()
    {
        var engine = MakeEngine();
        var key = await StoreTestFileAsync("demo-receipt.pdf", "application/pdf");
        var result = await engine.ExtractTextAsync(key);
        if (!TesseractAvailable())
        {
            Assert.False(result.Success);
            return;
        }
        // The demo PDF is an image-only PDF (no embedded text), so it must go through pdftoppm + tesseract.
        Assert.True(result.Success);
        Assert.Contains("CORNER CAFE", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("18.40", result.Text);
    }

    [Fact]
    public async Task Engine_MissingFile_ReturnsFailureNotException()
    {
        var engine = MakeEngine();
        var result = await engine.ExtractTextAsync("does-not-exist.png");
        Assert.False(result.Success);
        Assert.Null(result.Text);
    }

    // ---------- Processing service: upload -> OCR -> Ready, and reprocess ----------

    [Fact]
    public async Task Upload_SmallPng_QueuesOcr()
    {
        // The test factory caps uploads at 1024 bytes, so the queue wiring is proven with
        // a tiny valid PNG; the 61 KB demo receipt is exercised at the service level below.
        var (client, workspaceId, _) = await SetupWorkspaceAsync("ocr-queue");
        var tinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(tinyPng), "file", "tiny.png");
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", content);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
        Assert.Equal("Queued", doc.OcrStatus); // upload queues OCR (Phase 3 behaviour change from NotStarted)
    }

    [Fact]
    public async Task Process_DemoReceipt_EndToEnd_MarksReadyWithExtractedFields()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("ocr-e2e");
        // Seed the demo receipt straight into storage + DB (the HTTP upload cap is 1 KiB in tests).
        var docId = await SeedDemoReceiptAsync(workspaceId, userId);

        // Run the worker's processing step directly (the hosted worker does this in production).
        using var scope = _factory.Services.CreateScope();
        var processing = scope.ServiceProvider.GetRequiredService<OcrProcessingService>();
        Assert.True(await processing.ProcessAsync(workspaceId, docId, userId));

        var after = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}");
        after.EnsureSuccessStatusCode();
        var detail = await after.Content.ReadFromJsonAsync<DocDetail>() ?? throw new InvalidOperationException("Empty document response.");
        Assert.Equal("Ready", detail.Status);
        Assert.Equal("Completed", detail.OcrStatus);
        Assert.Contains("CORNER CAFE", detail.OcrText ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("THE CORNER CAFE", detail.ExtractedVendor);
        Assert.Equal(new DateTime(2026, 11, 24), detail.ExtractedDate);
        Assert.Equal(18.40m, detail.ExtractedTotal);
        Assert.Equal("£", detail.ExtractedCurrency);
        Assert.Equal("Receipt", detail.SuggestedCategory);
        Assert.NotNull(detail.OcrConfidence);
    }

    [Fact]
    public async Task Reprocess_AfterFailure_ResetsToQueuedAndProcessesAgain()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("ocr-reproc");
        var docId = await SeedDemoReceiptAsync(workspaceId, userId);

        // Simulate a failed run: mark Failed directly, then reprocess via the API.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Documents.FindAsync(docId) ?? throw new InvalidOperationException("Document row missing.");
            row.Status = DocumentStatus.Failed;
            row.OcrStatus = OcrStatus.Failed;
            await db.SaveChangesAsync();
        }

        var reprocess = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/reprocess", null);
        Assert.Equal(HttpStatusCode.OK, reprocess.StatusCode);
        var queued = await reprocess.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty reprocess response.");
        Assert.Equal("Queued", queued.OcrStatus);
        Assert.Null(queued.ExtractedVendor);

        using var scope2 = _factory.Services.CreateScope();
        var processing = scope2.ServiceProvider.GetRequiredService<OcrProcessingService>();
        Assert.True(await processing.ProcessAsync(workspaceId, docId, userId));
        var after = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}");
        var detail = await after.Content.ReadFromJsonAsync<DocDetail>() ?? throw new InvalidOperationException("Empty document response.");
        Assert.Equal("Ready", detail.Status);
        Assert.Equal("Receipt", detail.SuggestedCategory);
    }

    [Fact]
    public async Task Reprocess_AlreadyQueued_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("ocr-reproc400");
        var docId = await SeedDemoReceiptAsync(workspaceId, "seed-user");
        var reprocess = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/reprocess", null);
        Assert.Equal(HttpStatusCode.BadRequest, reprocess.StatusCode);
    }

    // ---------- helpers ----------

    private static bool TesseractAvailable()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PATH"))
           && (File.Exists("/usr/bin/tesseract") || File.Exists("/usr/local/bin/tesseract"));

    private TesseractOcrEngine MakeEngine()
    {
        using var scope = _factory.Services.CreateScope();
        // Resolve storage from the factory so the engine reads the same temp store the API uses.
        var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        return new TesseractOcrEngine(storage,
            Options.Create(new OcrSettings()),
            NullLogger<TesseractOcrEngine>.Instance);
    }

    private async Task<string> StoreTestFileAsync(string fileName, string mimeType)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        await using var stream = File.OpenRead(TestData(fileName));
        return await storage.SaveAsync(stream, fileName, mimeType);
    }

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    /// <summary>Seeds the demo receipt PNG into storage + DB as a Queued document (bypasses the 1 KiB HTTP upload cap in tests).</summary>
    private async Task<Guid> SeedDemoReceiptAsync(Guid workspaceId, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var png = await File.ReadAllBytesAsync(TestData("demo-receipt.png"));
        string key;
        await using (var stream = new MemoryStream(png))
            key = await storage.SaveAsync(stream, "demo-receipt.png", "image/png");
        var doc = new Document
        {
            WorkspaceId = workspaceId,
            OriginalFileName = "demo-receipt.png",
            StorageKey = key,
            Sha256Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)),
            FileSizeBytes = png.Length,
            MimeType = "image/png",
            Status = DocumentStatus.Uploaded,
            OcrStatus = OcrStatus.Queued,
            CreatedById = userId
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private record DocBody(Guid Id, string OriginalFileName, string Status, string OcrStatus, string? ExtractedVendor);
    private record DocDetail(Guid Id, string Status, string OcrStatus, string? OcrText, double? OcrConfidence,
        string? ExtractedVendor, DateTime? ExtractedDate, decimal? ExtractedTotal, string? ExtractedCurrency,
        string? SuggestedCategory);
}
