using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

public class IsolationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public IsolationTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private async Task<(HttpClient OwnerClient, Guid WorkspaceId, Guid ArchiveId)> SetupOwnerWorkspaceAsync(string prefix)
    {
        var ownerClient = _factory.CreateClient();
        var owner = await TestHelpers.RegisterAndLoginAsync(ownerClient, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(ownerClient, owner.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(ownerClient);
        var archive = await ownerClient.PostAsJsonAsync($"/api/workspaces/{workspaceId}/archives",
            new { Name = "Private Archive", Description = "owner only" });
        archive.EnsureSuccessStatusCode();
        var archiveBody = await archive.Content.ReadFromJsonAsync<IdBody>() ?? throw new InvalidOperationException("Empty archive response.");
        return (ownerClient, workspaceId, archiveBody.Id);
    }

    [Fact]
    public async Task NonMember_CannotGetWorkspace_Archives_Dashboard_OrAuditLogs()
    {
        var (_, workspaceId, archiveId) = await SetupOwnerWorkspaceAsync("iso-owner");

        var strangerClient = _factory.CreateClient();
        var stranger = await TestHelpers.RegisterAndLoginAsync(strangerClient, TestHelpers.UniqueEmail("iso-stranger"));
        TestHelpers.Authed(strangerClient, stranger.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/archives")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/archives/{archiveId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/dashboard")).StatusCode);
        var audit = await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/audit-logs");
        Assert.True(audit.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"Expected 403/404, got {audit.StatusCode}");
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/documents")).StatusCode);
    }

    [Fact]
    public async Task NonMember_ListMine_DoesNotLeakOtherWorkspace()
    {
        var (_, workspaceId, _) = await SetupOwnerWorkspaceAsync("iso-owner2");

        var strangerClient = _factory.CreateClient();
        var stranger = await TestHelpers.RegisterAndLoginAsync(strangerClient, TestHelpers.UniqueEmail("iso-stranger2"));
        TestHelpers.Authed(strangerClient, stranger.AccessToken);
        var list = await strangerClient.GetAsync("/api/workspaces");
        list.EnsureSuccessStatusCode();
        var workspaces = await list.Content.ReadFromJsonAsync<List<IdBody>>() ?? [];
        Assert.DoesNotContain(workspaces, w => w.Id == workspaceId);
    }

    [Fact]
    public async Task NonMember_CannotGetDocument_SeededInOtherWorkspace()
    {
        var ownerClient = _factory.CreateClient();
        var owner = await TestHelpers.RegisterAndLoginAsync(ownerClient, TestHelpers.UniqueEmail("iso-doc-owner"));
        TestHelpers.Authed(ownerClient, owner.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(ownerClient);
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, owner.UserId);

        // Owner can read it.
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}")).StatusCode);

        var strangerClient = _factory.CreateClient();
        var stranger = await TestHelpers.RegisterAndLoginAsync(strangerClient, TestHelpers.UniqueEmail("iso-doc-stranger"));
        TestHelpers.Authed(strangerClient, stranger.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/workspaces/{workspaceId}/documents/{docId}")).StatusCode);
    }

    private record IdBody(Guid Id);
}
