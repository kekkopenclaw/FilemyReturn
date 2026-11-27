using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

public class AuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AuthorizationTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    [Fact]
    public async Task ListWorkspaces_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyMember_CannotCreateArchive_Returns403()
    {
        var ownerClient = _factory.CreateClient();
        var owner = await TestHelpers.RegisterAndLoginAsync(ownerClient, TestHelpers.UniqueEmail("owner-ro"));
        TestHelpers.Authed(ownerClient, owner.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(ownerClient);

        var readerClient = _factory.CreateClient();
        var reader = await TestHelpers.RegisterAndLoginAsync(readerClient, TestHelpers.UniqueEmail("reader"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, reader.Email, "ReadOnly");

        TestHelpers.Authed(readerClient, reader.AccessToken);
        var create = await readerClient.PostAsJsonAsync($"/api/workspaces/{workspaceId}/archives",
            new { Name = "Nope", Description = "should fail" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyMember_CannotAddMember_Returns403()
    {
        var ownerClient = _factory.CreateClient();
        var owner = await TestHelpers.RegisterAndLoginAsync(ownerClient, TestHelpers.UniqueEmail("owner-ro2"));
        TestHelpers.Authed(ownerClient, owner.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(ownerClient);

        var readerClient = _factory.CreateClient();
        var reader = await TestHelpers.RegisterAndLoginAsync(readerClient, TestHelpers.UniqueEmail("reader2"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, reader.Email, "ReadOnly");

        var thirdClient = _factory.CreateClient();
        var third = await TestHelpers.RegisterAndLoginAsync(thirdClient, TestHelpers.UniqueEmail("third"));
        TestHelpers.Authed(readerClient, reader.AccessToken);
        var add = await readerClient.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members",
            new { Email = third.Email, Role = "StandardUser" });
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
    }

    [Fact]
    public async Task StandardUser_CanCreateArchive_ButCannotDeleteWorkspace()
    {
        var ownerClient = _factory.CreateClient();
        var owner = await TestHelpers.RegisterAndLoginAsync(ownerClient, TestHelpers.UniqueEmail("owner-std"));
        TestHelpers.Authed(ownerClient, owner.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(ownerClient);

        var stdClient = _factory.CreateClient();
        var std = await TestHelpers.RegisterAndLoginAsync(stdClient, TestHelpers.UniqueEmail("standard"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, std.Email, "StandardUser");

        TestHelpers.Authed(stdClient, std.AccessToken);
        var create = await stdClient.PostAsJsonAsync($"/api/workspaces/{workspaceId}/archives",
            new { Name = "Allowed", Description = "standard user may create" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var delete = await stdClient.DeleteAsync($"/api/workspaces/{workspaceId}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyMember_CannotReadAuditLogs_Returns403()
    {
        var ownerClient = _factory.CreateClient();
        var owner = await TestHelpers.RegisterAndLoginAsync(ownerClient, TestHelpers.UniqueEmail("owner-audit"));
        TestHelpers.Authed(ownerClient, owner.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(ownerClient);

        var readerClient = _factory.CreateClient();
        var reader = await TestHelpers.RegisterAndLoginAsync(readerClient, TestHelpers.UniqueEmail("reader-audit"));
        await TestHelpers.AddMemberAsync(ownerClient, workspaceId, reader.Email, "ReadOnly");

        TestHelpers.Authed(readerClient, reader.AccessToken);
        var logs = await readerClient.GetAsync($"/api/workspaces/{workspaceId}/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, logs.StatusCode);
    }
}
