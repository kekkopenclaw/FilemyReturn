using System.Net;
using System.Net.Http.Json;
using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>
/// Security audit tests (2026-11-27): every finding from the audit has a test here.
/// Refresh-token reuse / revocation, failed-login + download audit, enumeration
/// resistance, member removal / role change, workspace-delete file + audit cleanup,
/// duplicate-upload race, extension/MIME match, audit paging bounds, security headers.
/// </summary>
public class SecurityAuditTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public SecurityAuditTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

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

    private async Task<(HttpClient Client, TestHelpers.AuthSession Session, Guid WorkspaceId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, session, workspaceId);
    }

    private record AuthBody(string AccessToken, string RefreshToken, UserBody User);
    private record UserBody(string Id, string Email);
    private record DocBody(Guid Id, string OriginalFileName);
    private record MemberBody(string UserId, string Email, string? DisplayName, string Role, DateTime JoinedAt);

    [Fact]
    public async Task Refresh_ReuseOfRevokedToken_RevokesWholeFamily()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("reuse"));
        // Rotate once: old token is now revoked, new token is the live one.
        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = session.RefreshToken });
        refreshed.EnsureSuccessStatusCode();
        var liveToken = TestHelpers.RefreshCookieValue(refreshed);

        // Replaying the revoked token must fail AND kill the whole family (theft response).
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        // The live rotated token must now be dead too — otherwise the stolen family survives.
        var liveAfterReuse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = liveToken });
        Assert.Equal(HttpStatusCode.Unauthorized, liveAfterReuse.StatusCode);
        // One audit row per reuse presentation (the first replay, then the rotated token
        // presented after the family was killed): both are distinct reuse attempts.
        Assert.Equal(2, TestHelpers.CountAudit(_factory, session.UserId, "Auth.RefreshTokenReuse"));
    }

    [Fact]
    public async Task ChangePassword_RevokesOldRefreshToken()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("pwrevoke"));
        TestHelpers.Authed(client, session.AccessToken);
        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { CurrentPassword = TestHelpers.Password, NewPassword = "NewPassword456!@#" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // The pre-change refresh token must no longer mint access tokens.
        var anon = _factory.CreateClient();
        var refresh = await anon.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Login with the new password still works and issues fresh tokens.
        var login = await anon.PostAsJsonAsync("/api/auth/login",
            new { Email = session.Email, Password = "NewPassword456!@#" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_AuditsFailedLogin()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("loginfail"));
        var bad = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = session.Email, Password = "WrongPassword123!@#" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, session.UserId, "Auth.LoginFailed"));
    }

    [Fact]
    public async Task Download_IsAudited()
    {
        var (client, session, workspaceId) = await SetupWorkspaceAsync("dl-audit");
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)));
        upload.EnsureSuccessStatusCode();
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");
        var download = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{doc.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, session.UserId, "Document.Downloaded"));
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsGenericError_WithoutEnumerating()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("dup");
        var first = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = TestHelpers.Password, DisplayName = "Dup" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = TestHelpers.Password, DisplayName = "Dup Again" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.DoesNotContain("already taken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Registration failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePasswordWithoutLogin_UnknownEmail_Returns401_Not404()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { Email = TestHelpers.UniqueEmail("ghost"), CurrentPassword = TestHelpers.Password, NewPassword = "NewPassword456!@#" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordWithoutLogin_NonFlaggedAccount_Returns401_Not403()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("noflag"));
        // A normal account is not flagged MustChangePassword: the without-login path must
        // not reveal that (no 403/404 distinction — same 401 as an unknown email).
        var anon = _factory.CreateClient();
        var response = await anon.PostAsJsonAsync("/api/auth/change-password",
            new { Email = session.Email, CurrentPassword = TestHelpers.Password, NewPassword = "NewPassword456!@#" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_OwnerCanRemove_LastOwnerCannotBeRemoved()
    {
        var (ownerClient, owner, workspaceId) = await SetupWorkspaceAsync("rm-owner");
        var memberClient = _factory.CreateClient();
        var member = await TestHelpers.RegisterAndLoginAsync(memberClient, TestHelpers.UniqueEmail("rm-member"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, member.Email, "StandardUser");

        var removed = await ownerClient.DeleteAsync($"/api/workspaces/{workspaceId}/members/{member.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var members = await ownerClient.GetAsync($"/api/workspaces/{workspaceId}/members");
        var list = await members.Content.ReadFromJsonAsync<List<MemberBody>>() ?? [];
        Assert.DoesNotContain(list, m => m.UserId == member.UserId);

        // The owner is the last owner: removing themselves must fail.
        var selfRemove = await ownerClient.DeleteAsync($"/api/workspaces/{workspaceId}/members/{owner.UserId}");
        Assert.Equal(HttpStatusCode.BadRequest, selfRemove.StatusCode);
    }

    [Fact]
    public async Task ChangeMemberRole_PromoteWorks_OwnerAndSystemAdminRolesRejected()
    {
        var (ownerClient, _, workspaceId) = await SetupWorkspaceAsync("role-owner");
        var memberClient = _factory.CreateClient();
        var member = await TestHelpers.RegisterAndLoginAsync(memberClient, TestHelpers.UniqueEmail("role-member"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, member.Email, "StandardUser");

        var promoted = await ownerClient.PatchAsJsonAsync($"/api/workspaces/{workspaceId}/members/{member.UserId}",
            new { Role = "ArchiveManager" });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var body = await promoted.Content.ReadFromJsonAsync<MemberBody>() ?? throw new InvalidOperationException("Empty role response.");
        Assert.Equal("ArchiveManager", body.Role);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await ownerClient.PatchAsJsonAsync($"/api/workspaces/{workspaceId}/members/{member.UserId}", new { Role = "WorkspaceOwner" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ownerClient.PatchAsJsonAsync($"/api/workspaces/{workspaceId}/members/{member.UserId}", new { Role = "SystemAdmin" })).StatusCode);
    }

    [Fact]
    public async Task DeleteWorkspace_RemovesStoredFiles_AndKeepsAuditLog()
    {
        var (client, session, workspaceId) = await SetupWorkspaceAsync("del-ws");
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(PdfBytes(200)));
        upload.EnsureSuccessStatusCode();
        var doc = await upload.Content.ReadFromJsonAsync<DocBody>() ?? throw new InvalidOperationException("Empty upload response.");

        string storageKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            storageKey = db.Documents.First(d => d.Id == doc.Id).StorageKey;
        }

        var deleted = await client.DeleteAsync($"/api/workspaces/{workspaceId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The stored file must be gone from disk (deleted data must not linger).
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            Assert.Null(await storage.OpenReadAsync(storageKey));
            // The audit trail survives the delete: the "Workspace.Deleted" record is kept,
            // with its workspace link nulled (SetNull) instead of cascade-deleted.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = db.AuditLogs.FirstOrDefault(a => a.UserId == session.UserId && a.Action == "Workspace.Deleted");
            Assert.NotNull(log);
            Assert.Null(log!.WorkspaceId);
        }
    }

    [Fact]
    public async Task Upload_DuplicateInsert_TranslatedTo409_Not500()
    {
        // The production race (two identical uploads passing the pre-check together) ends in
        // a unique-index violation inside the repository. That must surface as a 409
        // (DuplicateDocumentException), never a 500. True parallel HTTP cannot be tested
        // against this factory — it shares one in-memory SQLite connection, and concurrent
        // requests on it fail inside EF Core's connection setup (SQLite Error 5), which is
        // a test-environment limit, not the production path (pooled file connections).
        // So the race's second half is tested directly at the repository: inserting a
        // document whose (WorkspaceId, Sha256Hash) already exists.
        var (_, session, workspaceId) = await SetupWorkspaceAsync("race");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, session.UserId);
        string hash;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            hash = db.Documents.First(d => d.Id == docId).Sha256Hash;
        }

        using var scope2 = _factory.Services.CreateScope();
        var repo = scope2.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var duplicate = new Document
        {
            WorkspaceId = workspaceId,
            OriginalFileName = "copy.pdf",
            StorageKey = $"{Guid.NewGuid():N}.pdf",
            Sha256Hash = hash,
            FileSizeBytes = 1234,
            MimeType = "application/pdf",
            CreatedById = session.UserId
        };
        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(() => repo.AddAsync(duplicate));
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_ExtensionMismatch_Returns400()
    {
        var (client, _, workspaceId) = await SetupWorkspaceAsync("ext");
        // PNG bytes filed as .pdf: magic bytes and extension disagree — reject.
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        var upload = await client.PostAsync($"/api/workspaces/{workspaceId}/documents", PdfContent(png, "receipt.pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task AuditLogs_NegativeSkip_Returns200_Not500()
    {
        var (client, _, workspaceId) = await SetupWorkspaceAsync("skip");
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/audit-logs?skip=-1&take=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_ArePresent_OnHealth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff)
            && nosniff.Contains("nosniff"));
        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var frame)
            && frame.Contains("DENY"));
        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp)
            && csp.Any(v => v.Contains("frame-ancestors 'none'")));
        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var referrer)
            && referrer.Contains("no-referrer"));
    }
}
