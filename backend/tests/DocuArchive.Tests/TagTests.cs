using System.Net;
using System.Net.Http.Json;

namespace DocuArchive.Tests;

/// <summary>Phase 4 tags: CRUD, duplicate/colour validation, assign/unassign (idempotent),
/// filter by tag, delete cascades assignments, workspace isolation, role gating.</summary>
public class TagTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public TagTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private record TagBody(Guid Id, string Name, string Colour);
    private record DocBody(Guid Id, string OriginalFileName, List<TagBody>? Tags);

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private async Task<TagBody> CreateTagAsync(HttpClient client, Guid workspaceId, string name, string? colour = null)
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { Name = name, Colour = colour });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TagBody>() ?? throw new InvalidOperationException("Empty tag response.");
    }

    [Fact]
    public async Task Create_Returns201_WithPaletteColour_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-create");
        var tag = await CreateTagAsync(client, workspaceId, "Tax 2025");

        Assert.Equal("Tax 2025", tag.Name);
        Assert.Matches("^#[0-9a-f]{6}$", tag.Colour);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Tag.Created"));
    }

    [Fact]
    public async Task Create_WithExplicitColour_StoresIt()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("tag-colour");
        var tag = await CreateTagAsync(client, workspaceId, "Urgent", "#b42318");
        Assert.Equal("#b42318", tag.Colour);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns400_IncludingDifferentCase()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("tag-dup");
        await CreateTagAsync(client, workspaceId, "Receipts");

        var sameCase = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { Name = "Receipts" });
        Assert.Equal(HttpStatusCode.BadRequest, sameCase.StatusCode);
        var otherCase = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { Name = "RECEIPTS" });
        Assert.Equal(HttpStatusCode.BadRequest, otherCase.StatusCode);
    }

    [Fact]
    public async Task Create_BadColour_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("tag-badcolour");
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { Name = "Bad", Colour = "red" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsReadOnly_Returns403()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("tag-ro");
        var reader = _factory.CreateClient();
        var readerSession = await TestHelpers.RegisterAndLoginAsync(reader, TestHelpers.UniqueEmail("tag-ro-reader"));
        await TestHelpers.AddMemberAsync(client, workspaceId, readerSession.Email, "ReadOnly");
        TestHelpers.Authed(reader, readerSession.AccessToken);

        var response = await reader.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { Name = "Nope" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Assign_ReturnsTagList_AndDocumentShowsTag_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-assign");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var tag = await CreateTagAsync(client, workspaceId, "Expenses");

        var assign = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags", new { TagId = tag.Id });
        assign.EnsureSuccessStatusCode();
        var assigned = await assign.Content.ReadFromJsonAsync<List<TagBody>>();
        Assert.Single(assigned!);
        Assert.Equal("Expenses", assigned![0].Name);

        var doc = await client.GetFromJsonAsync<DocBody>($"/api/workspaces/{workspaceId}/documents/{docId}");
        Assert.Single(doc!.Tags!);
        Assert.Equal(tag.Id, doc.Tags![0].Id);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.Tagged"));
    }

    [Fact]
    public async Task Assign_Twice_IsIdempotent()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-idem");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var tag = await CreateTagAsync(client, workspaceId, "Twice");

        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags", new { TagId = tag.Id });
        var second = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags", new { TagId = tag.Id });
        second.EnsureSuccessStatusCode();
        var assigned = await second.Content.ReadFromJsonAsync<List<TagBody>>();
        Assert.Single(assigned!);
    }

    [Fact]
    public async Task Assign_OtherWorkspaceTag_Returns404()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-xws");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var (otherClient, otherWorkspaceId, _) = await SetupWorkspaceAsync("tag-xws-other");
        var foreignTag = await CreateTagAsync(otherClient, otherWorkspaceId, "Foreign");

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags", new { TagId = foreignTag.Id });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unassign_RemovesTag()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-unassign");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var tag = await CreateTagAsync(client, workspaceId, "Temp");
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags", new { TagId = tag.Id });

        var unassign = await client.DeleteAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags/{tag.Id}");
        unassign.EnsureSuccessStatusCode();
        var remaining = await unassign.Content.ReadFromJsonAsync<List<TagBody>>();
        Assert.Empty(remaining!);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.Untagged"));
    }

    [Fact]
    public async Task Delete_RemovesAssignments()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-del");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var tag = await CreateTagAsync(client, workspaceId, "Gone");
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/tags", new { TagId = tag.Id });

        var delete = await client.DeleteAsync($"/api/workspaces/{workspaceId}/tags/{tag.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var doc = await client.GetFromJsonAsync<DocBody>($"/api/workspaces/{workspaceId}/documents/{docId}");
        Assert.Empty(doc!.Tags!);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Tag.Deleted"));
    }

    [Fact]
    public async Task List_IsolatedAcrossWorkspaces()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("tag-iso");
        await CreateTagAsync(client, workspaceId, "Mine");
        var (otherClient, otherWorkspaceId, _) = await SetupWorkspaceAsync("tag-iso-other");

        var mine = await client.GetFromJsonAsync<List<TagBody>>($"/api/workspaces/{workspaceId}/tags");
        var others = await otherClient.GetFromJsonAsync<List<TagBody>>($"/api/workspaces/{otherWorkspaceId}/tags");
        Assert.Single(mine!);
        Assert.Empty(others!);
    }

    [Fact]
    public async Task Filter_ByTagId_ReturnsOnlyTaggedDocuments()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("tag-filter");
        var taggedId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var untaggedId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var tag = await CreateTagAsync(client, workspaceId, "Filter");
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{taggedId}/tags", new { TagId = tag.Id });

        var filtered = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/documents?tagId={tag.Id}");
        Assert.Single(filtered!);
        Assert.Equal(taggedId, filtered![0].Id);
        Assert.DoesNotContain(filtered!, d => d.Id == untaggedId);
    }
}
