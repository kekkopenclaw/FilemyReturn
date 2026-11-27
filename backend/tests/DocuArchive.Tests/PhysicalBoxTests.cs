using System.Net;
using System.Net.Http.Json;
using DocuArchive.Application;

namespace DocuArchive.Tests;

/// <summary>Phase 7 physical archive: box CRUD, duplicate/code validation, assign/unassign
/// (idempotent), box document listing, delete keeps documents, QR SVG endpoint,
/// workspace isolation, role gating, audit trail.</summary>
public class PhysicalBoxTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public PhysicalBoxTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private record BoxBody(Guid Id, string Name, string Code, string Location, string? Description, int DocumentCount);
    private record DocBody(Guid Id, string OriginalFileName, Guid? PhysicalBoxId, string? PhysicalBoxName);

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private async Task<BoxBody> CreateBoxAsync(HttpClient client, Guid workspaceId, string name = "Tax archive 2025", string location = "Shelf B, row 3")
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/boxes", new { Name = name, Location = location });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BoxBody>() ?? throw new InvalidOperationException("Empty box response.");
    }

    [Fact]
    public async Task Create_Returns201_WithCode_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-create");
        var box = await CreateBoxAsync(client, workspaceId);

        Assert.Equal("Tax archive 2025", box.Name);
        Assert.Equal("Shelf B, row 3", box.Location);
        Assert.Matches("^BOX-[A-Z2-9]{6}$", box.Code);
        Assert.Equal(0, box.DocumentCount);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "PhysicalBox.Created"));
    }

    [Fact]
    public void GenerateCode_MatchesPattern_AndIsUnique()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => PhysicalBoxService.GenerateCode()).ToList();
        Assert.All(codes, c => Assert.Matches("^BOX-[A-Z2-9]{6}$", c));
        Assert.Equal(100, codes.Distinct().Count());
    }

    [Fact]
    public async Task Create_DuplicateName_Returns400_IncludingDifferentCase()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-dup");
        await CreateBoxAsync(client, workspaceId, "Loft boxes");

        var sameCase = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/boxes", new { Name = "Loft boxes", Location = "Loft" });
        Assert.Equal(HttpStatusCode.BadRequest, sameCase.StatusCode);
        var otherCase = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/boxes", new { Name = "LOFT BOXES", Location = "Loft" });
        Assert.Equal(HttpStatusCode.BadRequest, otherCase.StatusCode);
    }

    [Fact]
    public async Task Create_MissingLocation_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-noloc");
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/boxes", new { Name = "No home", Location = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsReadOnly_Returns403()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-ro");
        var reader = _factory.CreateClient();
        var readerSession = await TestHelpers.RegisterAndLoginAsync(reader, TestHelpers.UniqueEmail("box-ro-reader"));
        await TestHelpers.AddMemberAsync(client, workspaceId, readerSession.Email, "ReadOnly");
        TestHelpers.Authed(reader, readerSession.AccessToken);

        var response = await reader.PostAsJsonAsync($"/api/workspaces/{workspaceId}/boxes", new { Name = "Nope", Location = "Nowhere" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetByCode_ReturnsBox_AndOtherWorkspaceGets404()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-code");
        var box = await CreateBoxAsync(client, workspaceId);

        var found = await client.GetAsync($"/api/workspaces/{workspaceId}/boxes/by-code/{box.Code}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var foundBody = await found.Content.ReadFromJsonAsync<BoxBody>();
        Assert.Equal(box.Id, foundBody!.Id);

        // Same code, different workspace: the box exists globally, but the
        // lookup is scoped — a code from another workspace is a 404, never a leak.
        var (_, otherWorkspaceId, _) = await SetupWorkspaceAsync("box-code-other");
        var leaked = await client.GetAsync($"/api/workspaces/{otherWorkspaceId}/boxes/by-code/{box.Code}");
        Assert.Equal(HttpStatusCode.NotFound, leaked.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesNameAndLocation_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-update");
        var box = await CreateBoxAsync(client, workspaceId);

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/boxes/{box.Id}",
            new { Name = "Moved boxes", Location = "Cupboard, top shelf", Description = "Moved in spring." });
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<BoxBody>();
        Assert.Equal("Moved boxes", updated!.Name);
        Assert.Equal("Cupboard, top shelf", updated.Location);
        Assert.Equal("Moved in spring.", updated.Description);
        // The printed code never changes — labels already stuck on the box stay valid.
        Assert.Equal(box.Code, updated.Code);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "PhysicalBox.Updated"));
    }

    [Fact]
    public async Task Update_DuplicateName_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-upddup");
        await CreateBoxAsync(client, workspaceId, "First box");
        var second = await CreateBoxAsync(client, workspaceId, "Second box");

        var response = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/boxes/{second.Id}",
            new { Name = "First box", Location = "Somewhere" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesBox_AndDocumentsLoseTheirBox_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-delete");
        var box = await CreateBoxAsync(client, workspaceId);
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = box.Id });

        var delete = await client.DeleteAsync($"/api/workspaces/{workspaceId}/boxes/{box.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "PhysicalBox.Deleted"));

        // The document survives — it just has no physical home any more.
        var doc = await client.GetFromJsonAsync<DocBody>($"/api/workspaces/{workspaceId}/documents/{docId}");
        Assert.Null(doc!.PhysicalBoxId);
        Assert.Null(doc.PhysicalBoxName);
    }

    [Fact]
    public async Task Assign_PutsDocumentInBox_ListsIt_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-assign");
        var box = await CreateBoxAsync(client, workspaceId);
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = box.Id });
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<DocBody>();
        Assert.Equal(box.Id, doc!.PhysicalBoxId);
        Assert.Equal(box.Name, doc.PhysicalBoxName);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.BoxAssigned"));

        var inBox = await client.GetFromJsonAsync<List<DocBody>>($"/api/workspaces/{workspaceId}/boxes/{box.Id}/documents");
        var onlyDoc = Assert.Single(inBox!);
        Assert.Equal(docId, onlyDoc.Id);

        var listed = await client.GetFromJsonAsync<List<BoxBody>>($"/api/workspaces/{workspaceId}/boxes");
        var onlyBox = Assert.Single(listed!);
        Assert.Equal(1, onlyBox.DocumentCount);
    }

    [Fact]
    public async Task Assign_Idempotent_NoSecondAuditEntry()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-assign2");
        var box = await CreateBoxAsync(client, workspaceId);
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);

        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = box.Id });
        var again = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = box.Id });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.BoxAssigned"));
    }

    [Fact]
    public async Task Assign_UnknownBox_Returns404()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-assign404");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, "seed-user");

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Assign_AsReadOnly_Returns403()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-assign-ro");
        var box = await CreateBoxAsync(client, workspaceId);
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var reader = _factory.CreateClient();
        var readerSession = await TestHelpers.RegisterAndLoginAsync(reader, TestHelpers.UniqueEmail("box-assign-ro-reader"));
        await TestHelpers.AddMemberAsync(client, workspaceId, readerSession.Email, "ReadOnly");
        TestHelpers.Authed(reader, readerSession.AccessToken);

        var response = await reader.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = box.Id });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unassign_RemovesDocumentFromBox_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-unassign");
        var box = await CreateBoxAsync(client, workspaceId);
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box", new { BoxId = box.Id });

        var response = await client.DeleteAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box");
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<DocBody>();
        Assert.Null(doc!.PhysicalBoxId);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.BoxUnassigned"));

        // Unassigning again is a no-op, not a second audit entry.
        await client.DeleteAsync($"/api/workspaces/{workspaceId}/documents/{docId}/physical-box");
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.BoxUnassigned"));
    }

    [Fact]
    public async Task Isolation_OtherWorkspaceCannotSeeOrUseBox()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("box-iso");
        var box = await CreateBoxAsync(client, workspaceId);
        var (_, otherWorkspaceId, _) = await SetupWorkspaceAsync("box-iso-other");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/workspaces/{otherWorkspaceId}/boxes/{box.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/workspaces/{otherWorkspaceId}/boxes/{box.Id}")).StatusCode);
        // Assigning a document to a box from another workspace is a 404, not a leak.
        var otherDoc = await TestHelpers.SeedDocumentAsync(_factory, otherWorkspaceId, userId);
        var assign = await client.PostAsJsonAsync($"/api/workspaces/{otherWorkspaceId}/documents/{otherDoc}/physical-box", new { BoxId = box.Id });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);
    }

    [Fact]
    public async Task Qr_ReturnsSvgImage()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-qr");
        var box = await CreateBoxAsync(client, workspaceId);

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/boxes/{box.Id}/qr");
        response.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType!.MediaType);
        var svg = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", svg.TrimStart());
        Assert.Contains("</svg>", svg);
        Assert.DoesNotContain(box.Code, svg); // the code is encoded in the modules, not printed as text
    }

    [Fact]
    public async Task Qr_UnknownBox_Returns404()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("box-qr404");
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/boxes/{Guid.NewGuid()}/qr");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
