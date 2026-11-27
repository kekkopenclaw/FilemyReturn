using System.Net;

namespace DocuArchive.Tests;

/// <summary>Failed file access is audited, not just successful access. A document
/// whose stored bytes are gone (e.g. the disk was wiped on a redeploy) returns 404
/// on download/preview — and that 404 must leave an audit row, or the missing file
/// is invisible to anyone reviewing the trail.</summary>
public class FailedDownloadAuditTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FailedDownloadAuditTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    [Fact]
    public async Task Download_MissingStoredFile_Returns404_AndIsAuditedAsDownloadFailed()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("fd-dl");
        // Seeded documents point at a storage key with no file behind it —
        // exactly the "row exists, bytes gone" case.
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);

        var download = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/download");

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.DownloadFailed"));
        Assert.Equal(0, TestHelpers.CountAudit(_factory, userId, "Document.Downloaded"));
    }

    [Fact]
    public async Task Preview_MissingStoredFile_Returns404_AndIsAuditedAsViewFailed()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("fd-pv");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);

        var preview = await client.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}/preview");

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.ViewFailed"));
        Assert.Equal(0, TestHelpers.CountAudit(_factory, userId, "Document.Viewed"));
    }

    [Fact]
    public async Task Download_MissingStoredFile_InAnotherWorkspace_IsNotAuditedForThisUser()
    {
        // Workspace isolation still holds on the failure path: a member of
        // workspace A must not be able to probe (or audit) workspace B's files.
        var (clientA, workspaceA, userA) = await SetupWorkspaceAsync("fd-iso-a");
        var (_, workspaceB, _) = await SetupWorkspaceAsync("fd-iso-b");
        // The document lives in workspace B; user A asks for it under workspace A.
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceB, userA);

        var download = await clientA.GetAsync($"/api/workspaces/{workspaceA}/documents/{docId}/download");

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(0, TestHelpers.CountAudit(_factory, userA, "Document.DownloadFailed"));
    }
}
