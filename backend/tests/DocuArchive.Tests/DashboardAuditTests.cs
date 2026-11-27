using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

public class DashboardAuditTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public DashboardAuditTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    [Fact]
    public async Task Dashboard_EmptyWorkspace_ReturnsZeroCounts()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("dash"));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardBody>() ?? throw new InvalidOperationException("Empty dashboard.");
        Assert.Equal(0, dashboard.ArchiveCount);
        Assert.Equal(0, dashboard.DocumentCount);
        Assert.Equal(1, dashboard.MemberCount);
        Assert.Equal(0, dashboard.DocumentsReady);
    }

    [Fact]
    public async Task AuditLog_WrittenOnLogin_AndWorkspaceCreate()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("audit"));
        Assert.True(TestHelpers.CountAudit(_factory, session.UserId, "Auth.Login") >= 1, "Expected an Auth.Login audit record.");

        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        Assert.True(TestHelpers.CountAudit(_factory, session.UserId, "Workspace.Created") >= 1, "Expected a Workspace.Created audit record.");

        // Owner (WorkspaceOwner >= ArchiveManager) can read the workspace audit log, which lists the creation.
        var logs = await client.GetAsync($"/api/workspaces/{workspaceId}/audit-logs");
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
        var entries = await logs.Content.ReadFromJsonAsync<List<AuditBody>>() ?? [];
        Assert.Contains(entries, e => e.Action == "Workspace.Created");
    }

    [Fact]
    public async Task AuditLog_HasNoEditOrDeleteEndpoint()
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail("audit2"));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);

        // There is no PUT/DELETE audit route: both must be 404/405, never a successful edit.
        var put = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/audit-logs/{Guid.NewGuid()}", new { Action = "Forged" });
        Assert.True(put.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"PUT audit returned {put.StatusCode}");
        var delete = await client.DeleteAsync($"/api/workspaces/{workspaceId}/audit-logs/{Guid.NewGuid()}");
        Assert.True(delete.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"DELETE audit returned {delete.StatusCode}");
    }

    private record DashboardBody(Guid WorkspaceId, int ArchiveCount, int DocumentCount, int MemberCount, int DocumentsReady, int DocumentsProcessing, int DocumentsFailed, int OcrPending);
    private record AuditBody(Guid Id, string Action);
}
