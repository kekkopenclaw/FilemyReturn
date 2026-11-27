using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocuArchive.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>Phase 8 backup: admin-only export of a consistent database snapshot
/// plus every stored file as one ZIP, with a manifest; audited; non-admins get
/// 403 and anonymous callers 401. PostgreSQL refuses the snapshot in-app
/// (pg_dump / provider backup instead) rather than shipping a broken export.</summary>
public class BackupTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public BackupTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    // Minimal PDF bytes (magic %PDF...). The test factory caps uploads at 1024 bytes.
    private static byte[] PdfBytes(int totalLength)
    {
        var header = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n"u8.ToArray();
        var bytes = new byte[totalLength];
        Array.Copy(header, bytes, Math.Min(header.Length, totalLength));
        for (var i = header.Length; i < totalLength; i++) bytes[i] = (byte)'A';
        return bytes;
    }

    /// <summary>Registers a user, flips the system-admin flag in the database, then logs in — so the issued JWT carries the SystemAdmin role claim.</summary>
    private async Task<TestHelpers.AuthSession> RegisterAdminAndLoginAsync(HttpClient client, string email)
    {
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = TestHelpers.Password, DisplayName = "Admin User" });
        register.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.IsSystemAdmin = true;
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = TestHelpers.Password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginBody>() ?? throw new InvalidOperationException("Empty login response.");
        return new TestHelpers.AuthSession(email, body.User.Id, body.AccessToken, TestHelpers.RefreshCookieValue(login));
    }

    private record LoginBody(string AccessToken, LoginUser User);
    private record LoginUser(string Id, string Email, bool IsSystemAdmin);
    private record InfoBody(string Provider, long DatabaseSizeBytes, int DocumentCount, int WorkspaceCount, int FileCount, long StorageSizeBytes);
    private record DocBody(Guid Id, string OriginalFileName, long FileSizeBytes);
    private record ManifestBody(int BackupVersion, string Provider, int DocumentCount, int WorkspaceCount, int FileCount, long DatabaseSizeBytes, long StorageSizeBytes);

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupAdminAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await RegisterAdminAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        Assert.True(session.AccessToken.Length > 0);
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private async Task<DocBody> UploadPdfAsync(HttpClient client, Guid workspaceId, string fileName = "test.pdf")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(PdfBytes(200)), "file", fileName);
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", content);
        upload.EnsureSuccessStatusCode();
        return await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
    }

    private string StorageKeyFor(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Documents.Where(d => d.Id == documentId).Select(d => d.StorageKey).Single();
    }

    [Fact]
    public async Task Info_AsAdmin_ReturnsCounts()
    {
        var (client, workspaceId, _) = await SetupAdminAsync("bk-info");
        await UploadPdfAsync(client, workspaceId);

        var info = await client.GetAsync("/api/admin/backup/info");
        info.EnsureSuccessStatusCode();
        var body = await info.Content.ReadFromJsonAsync<InfoBody>() ?? throw new InvalidOperationException("Empty info response.");

        Assert.Equal("Sqlite", body.Provider);
        Assert.True(body.DatabaseSizeBytes > 0);
        Assert.True(body.DocumentCount >= 1);
        Assert.True(body.WorkspaceCount >= 1);
        Assert.True(body.FileCount >= 1);
        Assert.True(body.StorageSizeBytes >= 200);
    }

    [Fact]
    public async Task Download_AsAdmin_ReturnsValidZip_WithSnapshotManifestAndFiles()
    {
        var (client, workspaceId, _) = await SetupAdminAsync("bk-dl");
        var doc = await UploadPdfAsync(client, workspaceId);
        var storageKey = StorageKeyFor(doc.Id);

        var download = await client.GetAsync("/api/admin/backup/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Matches(@"docuarchive-backup-\d{8}-\d{6}\.zip",
            download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName ?? "");

        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K'); // ZIP magic

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("database.sqlite", names);
        Assert.Contains("manifest.json", names);
        Assert.Contains($"files/{storageKey}", names);

        // The manifest describes the archive — and the counts must be real.
        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        var manifest = await JsonSerializer.DeserializeAsync<ManifestBody>(manifestStream)
            ?? throw new InvalidOperationException("Empty manifest.");
        Assert.Equal(1, manifest.BackupVersion);
        Assert.Equal("Sqlite", manifest.Provider);
        Assert.True(manifest.DocumentCount >= 1);
        Assert.True(manifest.FileCount >= 1);
        Assert.True(manifest.DatabaseSizeBytes > 0);

        // The stored file round-trips byte-for-byte.
        using var fileStream = zip.GetEntry($"files/{storageKey}")!.Open();
        using var fileMemory = new MemoryStream();
        await fileStream.CopyToAsync(fileMemory);
        Assert.Equal(PdfBytes(200), fileMemory.ToArray());

        // The snapshot is a real, openable SQLite database holding the documents.
        var snapshotPath = Path.Combine(Path.GetTempPath(), "docuarchive-backup-test-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            using (var snapshotStream = zip.GetEntry("database.sqlite")!.Open())
            await using (var snapshotFile = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write))
                await snapshotStream.CopyToAsync(snapshotFile);
            await using var connection = new SqliteConnection($"Data Source={snapshotPath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Documents";
            var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
            Assert.True(count >= 1);
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }

    [Fact]
    public async Task Download_AsAdmin_IsAudited()
    {
        var (client, _, userId) = await SetupAdminAsync("bk-audit");
        var download = await client.GetAsync("/api/admin/backup/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Backup.Downloaded"));
    }

    [Fact]
    public async Task Info_AsNonAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("bk-noadmin"));
        TestHelpers.Authed(client, session.AccessToken);

        var info = await client.GetAsync("/api/admin/backup/info");
        Assert.Equal(HttpStatusCode.Forbidden, info.StatusCode);
    }

    [Fact]
    public async Task Download_AsNonAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("bk-noadmin-dl"));
        TestHelpers.Authed(client, session.AccessToken);

        var download = await client.GetAsync("/api/admin/backup/download");
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
    }

    [Fact]
    public async Task Backup_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/backup/info")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/backup/download")).StatusCode);
    }

    [Fact]
    public async Task Snapshot_Postgres_ThrowsNotSupported()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Postgres" })
            .Build();
        var service = new DatabaseBackupService(db, config);
        var target = Path.Combine(Path.GetTempPath(), "docuarchive-backup-test-" + Guid.NewGuid().ToString("N") + ".sqlite");
        await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateSnapshotAsync(target));
        Assert.False(File.Exists(target));
    }
}
