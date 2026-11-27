using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

/// <summary>Phase 2 upload tests: validation (magic bytes, size), duplicates, download, history, authorisation.</summary>
public class UploadTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public UploadTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    // Minimal PDF bytes (magic %PDF...). The test factory caps uploads at 1024 bytes.
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

    private async Task<(HttpClient Client, Guid WorkspaceId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId);
    }

    [Fact]
    public async Task Upload_ValidPdf_Returns201_AndAppearsInListAndHistory()
    {
        var (client, workspaceId) = await SetupWorkspaceAsync("up-ok");
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
        Assert.Equal("test.pdf", doc.OriginalFileName);
        Assert.Equal("application/pdf", doc.MimeType);
        Assert.Equal(200, doc.FileSizeBytes);
        Assert.Equal("Uploaded", doc.Status);
        Assert.Equal("NotStarted", doc.OcrStatus);
        Assert.False(string.IsNullOrWhiteSpace(doc.Sha256Hash));

        var list = await client.GetAsync($"/api/workspaces/{workspaceId}/documents");
        list.EnsureSuccessStatusCode();
        var docs = await list.Content.ReadFromJsonAsync<List<DocBody>>() ?? [];
        Assert.Contains(docs, d => d.Id == doc.Id);

        var history = await client.GetAsync($"/api/workspaces/{workspaceId}/uploads");
        history.EnsureSuccessStatusCode();
        var entries = await history.Content.ReadFromJsonAsync<List<UploadBody>>() ?? [];
        Assert.Contains(entries, e => e.DocumentId == doc.Id && e.UploadedByEmail is not null);
    }

    [Fact]
    public async Task Upload_Download_ReturnsSameBytes()
    {
        var (client, workspaceId) = await SetupWorkspaceAsync("up-dl");
        var bytes = PdfBytes(300);
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(bytes));
        upload.EnsureSuccessStatusCode();
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");

        var download = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{doc.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_InvalidFileType_Returns400()
    {
        var (client, workspaceId) = await SetupWorkspaceAsync("up-bad");
        var text = System.Text.Encoding.UTF8.GetBytes("This is plain text, not a document at all.");
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(text, "notes.txt"));
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task Upload_Duplicate_Returns409()
    {
        var (client, workspaceId) = await SetupWorkspaceAsync("up-dup");
        var first = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200), "copy.pdf"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Upload_Oversized_Returns413()
    {
        var (client, workspaceId) = await SetupWorkspaceAsync("up-big");
        // Test factory caps uploads at 1024 bytes; this file is 2048.
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(2048)));
        Assert.Equal((HttpStatusCode)413, upload.StatusCode);
    }

    [Fact]
    public async Task Upload_ReadOnly_Returns403()
    {
        var (ownerClient, workspaceId) = await SetupWorkspaceAsync("up-ro-owner");
        var readerClient = _factory.CreateClient();
        var reader = await TestHelpers.RegisterAndLoginAsync(readerClient, TestHelpers.UniqueEmail("up-reader"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, reader.Email, "ReadOnly");
        TestHelpers.Authed(readerClient, reader.AccessToken);
        var upload = await readerClient.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)));
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
    }

    [Fact]
    public async Task Upload_NonMember_Returns404_AndDownload_NonMember_Returns404()
    {
        var (ownerClient, workspaceId) = await SetupWorkspaceAsync("up-nm-owner");
        var upload = await ownerClient.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)));
        upload.EnsureSuccessStatusCode();
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");

        var strangerClient = _factory.CreateClient();
        var stranger = await TestHelpers.RegisterAndLoginAsync(strangerClient, TestHelpers.UniqueEmail("up-stranger"));
        TestHelpers.Authed(strangerClient, stranger.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,
            (await strangerClient.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/documents/{doc.Id}/download")).StatusCode);
    }

    [Fact]
    public async Task Upload_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var upload = await client.PostAsync($"/api/workspaces/{Guid.NewGuid()}/documents", PdfContent(PdfBytes(200)));
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
    }

    private record DocBody(Guid Id, string OriginalFileName, string MimeType, long FileSizeBytes, string Sha256Hash, string Status, string OcrStatus);
    private record UploadBody(Guid DocumentId, string? UploadedByEmail);
}
