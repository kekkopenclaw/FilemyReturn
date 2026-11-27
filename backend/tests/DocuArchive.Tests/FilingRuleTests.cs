using System.Net;
using System.Net.Http.Json;
using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Tests;

/// <summary>Phase 4 filing rules: CRUD, validation (condition/action required,
/// unknown category, duplicate name, foreign tag/archive), role gating, workspace
/// isolation, matching (vendor/category/file name, priority order, snapshot
/// semantics), automatic filing after OCR, manual apply, stale-reference tolerance.</summary>
public class FilingRuleTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FilingRuleTests(CustomWebApplicationFactory factory) { _factory = factory; _factory.EnsureCreated(); }

    private record RuleBody(
        Guid Id, Guid WorkspaceId, string Name, bool IsEnabled, int Priority,
        string? VendorContains, string? Category, string? FileNameContains,
        Guid? AssignTagId, string? SetCategory, Guid? MoveToArchiveId,
        int AppliedCount, DateTime CreatedAt);
    private record TagBody(Guid Id, string Name, string Colour);
    private record ArchiveBody(Guid Id, string Name);
    private record DocBody(Guid Id, string OriginalFileName, string Status, Guid? ArchiveId, string? SuggestedCategory,
        string? ConfirmedCategory, bool NeedsReview, List<TagBody>? Tags);

    private async Task<(HttpClient Client, Guid WorkspaceId, string UserId)> SetupWorkspaceAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var session = await TestHelpers.RegisterAndLoginAsync(client, TestHelpers.UniqueEmail(prefix));
        TestHelpers.Authed(client, session.AccessToken);
        var workspaceId = await TestHelpers.CreateWorkspaceAsync(client);
        return (client, workspaceId, session.UserId);
    }

    private static object RuleJson(string name, string? vendor = null, string? category = null, string? fileName = null,
        Guid? assignTagId = null, string? setCategory = null, Guid? moveToArchiveId = null,
        bool isEnabled = true, int priority = 0)
        => new
        {
            Name = name, IsEnabled = isEnabled, Priority = priority,
            VendorContains = vendor, Category = category, FileNameContains = fileName,
            AssignTagId = assignTagId, SetCategory = setCategory, MoveToArchiveId = moveToArchiveId
        };

    private async Task<RuleBody> CreateRuleAsync(HttpClient client, Guid workspaceId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RuleBody>() ?? throw new InvalidOperationException("Empty rule response.");
    }

    private async Task<TagBody> CreateTagAsync(HttpClient client, Guid workspaceId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { Name = name, Colour = (string?)null });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TagBody>() ?? throw new InvalidOperationException("Empty tag response.");
    }

    private async Task<ArchiveBody> CreateArchiveAsync(HttpClient client, Guid workspaceId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/archives", new { Name = name, Description = (string?)null });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ArchiveBody>() ?? throw new InvalidOperationException("Empty archive response.");
    }

    [Fact]
    public async Task Create_Returns201_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-create");
        var tag = await CreateTagAsync(client, workspaceId, "Expenses");

        var rule = await CreateRuleAsync(client, workspaceId,
            RuleJson("Tesco expenses", vendor: "Tesco", assignTagId: tag.Id, setCategory: "Receipt"));

        Assert.Equal("Tesco expenses", rule.Name);
        Assert.True(rule.IsEnabled);
        Assert.Equal("Tesco", rule.VendorContains);
        Assert.Equal(tag.Id, rule.AssignTagId);
        Assert.Equal("Receipt", rule.SetCategory);
        Assert.Equal(0, rule.AppliedCount);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "FilingRule.Created"));
    }

    [Fact]
    public async Task Create_NoCondition_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-nocond");
        var tag = await CreateTagAsync(client, workspaceId, "T");

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("No condition", assignTagId: tag.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoAction_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-noact");

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("No action", vendor: "Tesco"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownCategory_Returns400()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-badcat");

        var badCondition = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("Bad condition", category: "Groceries", setCategory: "Receipt"));
        Assert.Equal(HttpStatusCode.BadRequest, badCondition.StatusCode);

        var badAction = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("Bad action", vendor: "Tesco", setCategory: "Groceries"));
        Assert.Equal(HttpStatusCode.BadRequest, badAction.StatusCode);
    }

    [Fact]
    public async Task Create_ForeignTagOrArchive_Returns404()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-foreign");
        var (otherClient, otherWorkspaceId, _) = await SetupWorkspaceAsync("rule-foreign-other");
        var foreignTag = await CreateTagAsync(otherClient, otherWorkspaceId, "Foreign");
        var foreignArchive = await CreateArchiveAsync(otherClient, otherWorkspaceId, "Foreign");

        var badTag = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("Foreign tag", vendor: "Tesco", assignTagId: foreignTag.Id));
        Assert.Equal(HttpStatusCode.NotFound, badTag.StatusCode);

        var badArchive = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("Foreign archive", vendor: "Tesco", moveToArchiveId: foreignArchive.Id));
        Assert.Equal(HttpStatusCode.NotFound, badArchive.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns400_IncludingDifferentCase()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-dup");
        await CreateRuleAsync(client, workspaceId, RuleJson("Tesco", vendor: "Tesco", setCategory: "Receipt"));

        var sameCase = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("Tesco", vendor: "Tesco", setCategory: "Receipt"));
        Assert.Equal(HttpStatusCode.BadRequest, sameCase.StatusCode);
        var otherCase = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("TESCO", vendor: "Tesco", setCategory: "Receipt"));
        Assert.Equal(HttpStatusCode.BadRequest, otherCase.StatusCode);
    }

    [Fact]
    public async Task Create_AsReadOnly_Returns403()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-ro");
        var reader = _factory.CreateClient();
        var readerSession = await TestHelpers.RegisterAndLoginAsync(reader, TestHelpers.UniqueEmail("rule-ro-reader"));
        await TestHelpers.AddMemberAsync(client, workspaceId, readerSession.Email, "ReadOnly");
        TestHelpers.Authed(reader, readerSession.AccessToken);

        var response = await reader.PostAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules",
            RuleJson("Nope", vendor: "Tesco", setCategory: "Receipt"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_TogglesEnabled_ChangesPriority_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-update");
        var rule = await CreateRuleAsync(client, workspaceId,
            RuleJson("Updatable", vendor: "Tesco", setCategory: "Receipt", priority: 1));

        var update = await client.PutAsJsonAsync($"/api/workspaces/{workspaceId}/filing-rules/{rule.Id}",
            RuleJson("Updatable", vendor: "Tesco", setCategory: "Invoice", isEnabled: false, priority: 5));
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<RuleBody>();
        Assert.False(updated!.IsEnabled);
        Assert.Equal(5, updated.Priority);
        Assert.Equal("Invoice", updated.SetCategory);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "FilingRule.Updated"));
    }

    [Fact]
    public async Task Delete_RemovesRule()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-del");
        var rule = await CreateRuleAsync(client, workspaceId,
            RuleJson("Gone", vendor: "Tesco", setCategory: "Receipt"));

        var delete = await client.DeleteAsync($"/api/workspaces/{workspaceId}/filing-rules/{rule.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var get = await client.GetAsync($"/api/workspaces/{workspaceId}/filing-rules/{rule.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "FilingRule.Deleted"));
    }

    [Fact]
    public async Task List_IsolatedAcrossWorkspaces()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-iso");
        await CreateRuleAsync(client, workspaceId, RuleJson("Mine", vendor: "Tesco", setCategory: "Receipt"));
        var (otherClient, otherWorkspaceId, _) = await SetupWorkspaceAsync("rule-iso-other");

        var mine = await client.GetFromJsonAsync<List<RuleBody>>($"/api/workspaces/{workspaceId}/filing-rules");
        var others = await otherClient.GetFromJsonAsync<List<RuleBody>>($"/api/workspaces/{otherWorkspaceId}/filing-rules");
        Assert.Single(mine!);
        Assert.Empty(others!);
    }

    [Fact]
    public async Task Get_OtherWorkspace_Returns404()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-get-xws");
        var rule = await CreateRuleAsync(client, workspaceId, RuleJson("Secret", vendor: "Tesco", setCategory: "Receipt"));
        var (otherClient, otherWorkspaceId, _) = await SetupWorkspaceAsync("rule-get-xws-other");

        var response = await otherClient.GetAsync($"/api/workspaces/{otherWorkspaceId}/filing-rules/{rule.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Apply_MatchingRule_AssignsTag_SetsCategory_MovesToArchive_AndIsAudited()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-apply");
        var tag = await CreateTagAsync(client, workspaceId, "Expenses");
        var archive = await CreateArchiveAsync(client, workspaceId, "Filed 2026");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Tesco expenses", vendor: "tesco", assignTagId: tag.Id, setCategory: "Receipt", moveToArchiveId: archive.Id));
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "tesco-receipt.png", "TESCO EXTRA", "Invoice");

        var apply = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        apply.EnsureSuccessStatusCode();
        var doc = await apply.Content.ReadFromJsonAsync<DocBody>();
        Assert.Single(doc!.Tags!);
        Assert.Equal("Expenses", doc.Tags![0].Name);
        // A rule's category counts as confirmed — it clears the review flag.
        Assert.Equal("Receipt", doc.ConfirmedCategory);
        Assert.Equal("Receipt", doc.SuggestedCategory);
        Assert.False(doc.NeedsReview);
        Assert.Equal(archive.Id, doc.ArchiveId);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.RuleFiled"));

        var rules = await client.GetFromJsonAsync<List<RuleBody>>($"/api/workspaces/{workspaceId}/filing-rules");
        Assert.Equal(1, rules![0].AppliedCount);
    }

    [Fact]
    public async Task Apply_NonMatchingRule_DoesNothing()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-nomatch");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Sainsbury only", vendor: "Sainsbury", setCategory: "Receipt"));
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "tesco-receipt.png", "Tesco Extra", "Receipt");

        var apply = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        apply.EnsureSuccessStatusCode();
        var doc = await apply.Content.ReadFromJsonAsync<DocBody>();
        Assert.Empty(doc!.Tags!);
        Assert.Null(doc.ConfirmedCategory);
        Assert.Equal(0, TestHelpers.CountAudit(_factory, userId, "Document.RuleFiled"));

        var rules = await client.GetFromJsonAsync<List<RuleBody>>($"/api/workspaces/{workspaceId}/filing-rules");
        Assert.Equal(0, rules![0].AppliedCount);
    }

    [Fact]
    public async Task Apply_DisabledRule_DoesNothing()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-disabled");
        var tag = await CreateTagAsync(client, workspaceId, "Expenses");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Off", vendor: "Tesco", assignTagId: tag.Id, isEnabled: false));
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId, "tesco.png", "Tesco", "Receipt", null);

        var apply = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        apply.EnsureSuccessStatusCode();
        var doc = await apply.Content.ReadFromJsonAsync<DocBody>();
        Assert.Empty(doc!.Tags!);
    }

    [Fact]
    public async Task Apply_FileNameCondition_MatchesWithoutOcr()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-filename");
        var tag = await CreateTagAsync(client, workspaceId, "Scans");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("PNG scans", fileName: ".png", assignTagId: tag.Id));
        // Plain seeded document: no vendor, no category, no OCR at all.
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);

        var apply = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        apply.EnsureSuccessStatusCode();
        var doc = await apply.Content.ReadFromJsonAsync<DocBody>();
        // The seeded file is test.pdf — ".png" does not match.
        Assert.Empty(doc!.Tags!);

        var pngId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId, "scan.png", null, null, null);
        var applyPng = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{pngId}/apply-rules", null);
        applyPng.EnsureSuccessStatusCode();
        var pngDoc = await applyPng.Content.ReadFromJsonAsync<DocBody>();
        Assert.Single(pngDoc!.Tags!);
    }

    [Fact]
    public async Task Apply_TwoRules_LastWinsForCategory_AndBothCount()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-order");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("First", vendor: "Tesco", setCategory: "Receipt", priority: 0));
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Second", vendor: "Tesco", setCategory: "Invoice", priority: 10));
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "tesco.png", "Tesco", "Other");

        var apply = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        apply.EnsureSuccessStatusCode();
        var doc = await apply.Content.ReadFromJsonAsync<DocBody>();
        // Both rules matched; the later (higher priority number) wins the category.
        Assert.Equal("Invoice", doc!.ConfirmedCategory);

        var rules = await client.GetFromJsonAsync<List<RuleBody>>($"/api/workspaces/{workspaceId}/filing-rules");
        Assert.All(rules!, r => Assert.Equal(1, r.AppliedCount));
    }

    [Fact]
    public async Task Apply_StaleTagReference_SkipsAction_AndOtherActionsStillApply()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-stale");
        var tag = await CreateTagAsync(client, workspaceId, "Doomed");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Stale", vendor: "Tesco", assignTagId: tag.Id, setCategory: "Receipt"));
        await client.DeleteAsync($"/api/workspaces/{workspaceId}/tags/{tag.Id}");
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId,
            "tesco.png", "Tesco", "Invoice");

        var apply = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        apply.EnsureSuccessStatusCode();
        var doc = await apply.Content.ReadFromJsonAsync<DocBody>();
        Assert.Empty(doc!.Tags!);
        // The category action still applied; the stale tag was skipped, not fatal.
        Assert.Equal("Receipt", doc!.ConfirmedCategory);
        Assert.Equal(1, TestHelpers.CountAudit(_factory, userId, "Document.RuleFiled"));
    }

    [Fact]
    public async Task Apply_AsReadOnly_Returns403()
    {
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-apply-ro");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Any", vendor: "Tesco", setCategory: "Receipt"));
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);
        var reader = _factory.CreateClient();
        var readerSession = await TestHelpers.RegisterAndLoginAsync(reader, TestHelpers.UniqueEmail("rule-apply-ro-reader"));
        await TestHelpers.AddMemberAsync(client, workspaceId, readerSession.Email, "ReadOnly");
        TestHelpers.Authed(reader, readerSession.AccessToken);

        var response = await reader.PostAsync($"/api/workspaces/{workspaceId}/documents/{docId}/apply-rules", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Apply_UnknownDocument_Returns404()
    {
        var (client, workspaceId, _) = await SetupWorkspaceAsync("rule-apply-404");
        var response = await client.PostAsync($"/api/workspaces/{workspaceId}/documents/{Guid.NewGuid()}/apply-rules", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OcrFailure_DoesNotRunRules()
    {
        // The OCR hook must only file documents that actually finished OCR.
        // A document whose stored file is gone fails OCR, so no rule may apply
        // to it — even a file-name rule that would otherwise match.
        var (client, workspaceId, userId) = await SetupWorkspaceAsync("rule-ocr");
        await CreateRuleAsync(client, workspaceId,
            RuleJson("Never on failure", fileName: "test", setCategory: "Receipt"));
        var docId = await TestHelpers.SeedDocumentAsync(_factory, workspaceId, userId);

        // Queue the document (its storage file does not exist, so OCR fails).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeded = await db.Documents.FirstAsync(d => d.Id == docId);
            seeded.OcrStatus = OcrStatus.Queued;
            await db.SaveChangesAsync();
        }

        using var processScope = _factory.Services.CreateScope();
        var ocr = processScope.ServiceProvider.GetRequiredService<OcrProcessingService>();
        Assert.True(await ocr.ProcessAsync(workspaceId, docId, userId));

        var doc = await client.GetFromJsonAsync<DocBody>($"/api/workspaces/{workspaceId}/documents/{docId}");
        Assert.Equal("Failed", doc!.Status);
        Assert.Null(doc.ConfirmedCategory);
        Assert.Equal(0, TestHelpers.CountAudit(_factory, userId, "Document.RuleFiled"));
    }

    [Fact]
    public void Matches_CaseInsensitiveVendor_AndExactCategory()
    {
        var doc = new Document
        {
            WorkspaceId = Guid.NewGuid(),
            OriginalFileName = "TESCO-receipt.PNG",
            StorageKey = "x",
            Sha256Hash = "x",
            MimeType = "image/png",
            CreatedById = "u1",
            ExtractedVendor = "Tesco Extra",
            SuggestedCategory = "Receipt"
        };
        Assert.True(FilingRuleService.Matches(new FilingRule { Name = "a", VendorContains = "tesco" }, doc));
        Assert.True(FilingRuleService.Matches(new FilingRule { Name = "a", FileNameContains = ".png" }, doc));
        Assert.True(FilingRuleService.Matches(new FilingRule { Name = "a", Category = "Receipt" }, doc));
        Assert.False(FilingRuleService.Matches(new FilingRule { Name = "a", Category = "Invoice" }, doc));
        Assert.False(FilingRuleService.Matches(new FilingRule { Name = "a", VendorContains = "Sainsbury" }, doc));
        // A rule with no conditions never matches.
        Assert.False(FilingRuleService.Matches(new FilingRule { Name = "a" }, doc));
        // Confirmed category wins over the suggestion for matching.
        doc.ConfirmedCategory = "Invoice";
        Assert.True(FilingRuleService.Matches(new FilingRule { Name = "a", Category = "Invoice" }, doc));
        Assert.False(FilingRuleService.Matches(new FilingRule { Name = "a", Category = "Receipt" }, doc));
    }
}
