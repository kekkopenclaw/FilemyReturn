using System.Net;
using System.Net.Http.Json;
using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>Phase 9 monitoring: admin-only GET /api/admin/monitoring returns
/// a read-only health snapshot — uptime, version, counts by document/OCR
/// status, review-queue depth, storage, failed logins in the last 24h, last
/// backup. Non-admins get 403 and anonymous callers 401, like the backup
/// endpoints. Nothing here writes or changes state.</summary>
public class MonitoringTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public MonitoringTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

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
    private record DocBody(Guid Id, string OriginalFileName, long FileSizeBytes);
    private record CountsBody(int Total, int Uploaded, int Processing, int Ready, int Failed,
        int OcrQueued, int OcrProcessing, int OcrFailed, int NeedsReview);
    private record StatusBody(string Version, string? Commit, double UptimeSeconds,
        string DatabaseProvider, long DatabaseSizeBytes,
        int UserCount, int WorkspaceCount, int DocumentCount,
        CountsBody DocumentsByStatus,
        int FileCount, long StorageSizeBytes,
        int FailedLoginsLast24h,
        DateTime? LastBackupAtUtc, string? LastBackupDetails);

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId, string Email)> SetupAdminAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail(prefix);
        var session = await RegisterAdminAndLoginAsync(client, email);
        Assert.True(session.AccessToken.Length > 0);
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId, email);
    }

    private async Task<DocBody> UploadPdfAsync(HttpClient client, Guid workspaceId, string fileName = "test.pdf")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(PdfBytes(200)), "file", fileName);
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", content);
        upload.EnsureSuccessStatusCode();
        return await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
    }

    private async Task<StatusBody> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/admin/monitoring");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StatusBody>()
            ?? throw new InvalidOperationException("Empty monitoring response.");
    }

    [Fact]
    public async Task AsAdmin_ReturnsCounts()
    {
        var (client, workspaceId, _, _) = await SetupAdminAsync("mon-info");
        await UploadPdfAsync(client, workspaceId);

        var status = await GetStatusAsync(client);

        Assert.Equal("Sqlite", status.DatabaseProvider);
        Assert.True(status.DatabaseSizeBytes > 0);
        Assert.True(status.UserCount >= 1);
        Assert.True(status.WorkspaceCount >= 1);
        Assert.True(status.DocumentCount >= 1);
        Assert.True(status.DocumentsByStatus.Total >= 1);
        Assert.True(status.DocumentsByStatus.Uploaded >= 1);
        Assert.Equal(status.DocumentCount, status.DocumentsByStatus.Total);
        Assert.True(status.FileCount >= 1);
        Assert.True(status.StorageSizeBytes >= 200);
        Assert.True(status.UptimeSeconds > 0);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
        Assert.True(status.FailedLoginsLast24h >= 0);
        // No backup has been downloaded yet in this test run.
        Assert.Null(status.LastBackupAtUtc);
        Assert.Null(status.LastBackupDetails);
    }

    [Fact]
    public async Task FailedLogin_CountsInLast24h()
    {
        var (client, _, _, email) = await SetupAdminAsync("mon-fail");
        var before = await GetStatusAsync(client);

        // One failed login for a real user (wrong password): AuthService logs Auth.LoginFailed.
        // A failed login for an unknown email is NOT audited (non-enumerating errors),
        // so the count must move by exactly one for the known email.
        var failed = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);

        var after = await GetStatusAsync(client);
        Assert.Equal(before.FailedLoginsLast24h + 1, after.FailedLoginsLast24h);
    }

    [Fact]
    public async Task LastBackup_PresentAfterDownload()
    {
        var (client, workspaceId, _, _) = await SetupAdminAsync("mon-backup");
        await UploadPdfAsync(client, workspaceId);

        var download = await client.GetAsync("/api/admin/backup/download");
        download.EnsureSuccessStatusCode();

        var status = await GetStatusAsync(client);
        Assert.NotNull(status.LastBackupAtUtc);
        Assert.NotNull(status.LastBackupDetails);
        Assert.Contains("docuarchive-backup-", status.LastBackupDetails);
    }

    [Fact]
    public async Task NeedsReview_QueueDepthIsReported()
    {
        var (client, workspaceId, _, _) = await SetupAdminAsync("mon-review");
        var doc = await UploadPdfAsync(client, workspaceId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Documents.SingleAsync(d => d.Id == doc.Id);
            row.NeedsReview = true;
            await db.SaveChangesAsync();
        }

        var status = await GetStatusAsync(client);
        Assert.True(status.DocumentsByStatus.NeedsReview >= 1);
    }

    [Fact]
    public async Task NonAdmin_Gets403()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("mon-user");
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = TestHelpers.Password, DisplayName = "Plain User" });
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = TestHelpers.Password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginBody>() ?? throw new InvalidOperationException("Empty login response.");
        TestHelpers.Authed(client, body.AccessToken);

        var response = await client.GetAsync("/api/admin/monitoring");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Gets401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/monitoring");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
