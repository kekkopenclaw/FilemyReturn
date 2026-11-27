using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

/// <summary>Phase 6 viewer tests: inline preview (Content-Disposition: inline) for
/// browser-renderable types, 400 for the rest, same workspace isolation as download.</summary>
public class PreviewTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PreviewTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    // Minimal PDF bytes (magic %PDF...). The test factory caps uploads at 1024 bytes.
    private static byte[] PdfBytes(int totalLength)
    {
        var header = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n"u8.ToArray();
        var bytes = new byte[totalLength];
        Array.Copy(header, bytes, Math.Min(header.Length, totalLength));
        for (var i = header.Length; i < totalLength; i++) bytes[i] = (byte)'A';
        return bytes;
    }

    // Minimal PNG: 8-byte signature + padding. Magic-byte detection only needs the header.
    private static byte[] PngBytes(int totalLength)
    {
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var bytes = new byte[totalLength];
        Array.Copy(header, bytes, Math.Min(header.Length, totalLength));
        return bytes;
    }

    // Minimal TIFF: little-endian "II*\0" magic.
    private static byte[] TiffBytes(int totalLength)
    {
        var header = new byte[] { 0x49, 0x49, 0x2A, 0x00 };
        var bytes = new byte[totalLength];
        Array.Copy(header, bytes, Math.Min(header.Length, totalLength));
        return bytes;
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        return content;
    }

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private async Task<Guid> UploadAsync(HttpClient client, Guid workspaceId, byte[] bytes, string fileName)
    {
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", FileContent(bytes, fileName));
        upload.EnsureSuccessStatusCode();
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
        return doc.Id;
    }

    [Fact]
    public async Task Preview_Pdf_Returns200_Inline_AndSameBytes_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("pv-pdf");
        var bytes = PdfBytes(300);
        var docId = await UploadAsync(client, workspaceId, bytes, "test.pdf");

        var preview = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("application/pdf", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", preview.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(bytes, await preview.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.Viewed"));
    }

    [Fact]
    public async Task Preview_Png_Returns200_Inline()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("pv-png");
        var docId = await UploadAsync(client, workspaceId, PngBytes(64), "scan.png");

        var preview = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", preview.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Preview_Tiff_Returns400_UseDownloadInstead()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("pv-tiff");
        var docId = await UploadAsync(client, workspaceId, TiffBytes(64), "scan.tif");

        var preview = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/preview");
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);

        // The same file still downloads fine.
        var download = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task Preview_NonMember_Returns404()
    {
        var (ownerClient, workspaceId, _) = await SetupWorkspaceAsync("pv-nm-owner");
        var docId = await UploadAsync(ownerClient, workspaceId, PdfBytes(200), "test.pdf");

        var strangerClient = _factory.CreateClient();
        var stranger = await TestHelpers.RegisterAndLoginAsync(strangerClient, TestHelpers.UniqueEmail("pv-stranger"));
        TestHelpers.Authed(strangerClient, stranger.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,
            (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/preview")).StatusCode);
    }

    [Fact]
    public async Task Preview_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/preview")).StatusCode);
    }

    private record DocBody(Guid Id, string OriginalFileName, string MimeType, long FileSizeBytes, string Sha256Hash, string Status, string OcrStatus);
}
