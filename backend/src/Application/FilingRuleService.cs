using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>
/// Filing rules (Phase 4 organisation). A rule says: when a document matches these
/// conditions, file it this way. Rules are workspace-scoped and evaluated in
/// Priority order (then CreatedAt) after OCR finishes successfully, and on demand
/// via the manual apply endpoint. All enabled matching rules apply; for the
/// single-value actions (set category, move to archive) the last matching rule
/// wins. Creating, updating and deleting rules requires StandardUser+.
///
/// A category set by a rule counts as confirmed: the rule is a decision its owner
/// configured in advance, exactly like a human confirming a category in the review
/// queue — so it sets ConfirmedCategory (and the suggestion, for display) and
/// clears NeedsReview. Tag assignment is idempotent, and a rule whose referenced
/// tag or archive was deleted skips that action only (never fails the whole
/// evaluation); the skip is noted in the audit trail. Every evaluation that
/// applies at least one rule writes one "Document.RuleFiled" audit record.
/// </summary>
public class FilingRuleService
{
    private readonly IFilingRuleRepository _rules;
    private readonly ITagRepository _tags;
    private readonly IArchiveRepository _archives;
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceAccessService _access;
    private readonly IAuditService _audit;

    public FilingRuleService(
        IFilingRuleRepository rules,
        ITagRepository tags,
        IArchiveRepository archives,
        IDocumentRepository documents,
        IWorkspaceAccessService access,
        IAuditService audit)
    {
        _rules = rules; _tags = tags; _archives = archives;
        _documents = documents; _access = access; _audit = audit;
    }

    private static FilingRuleDto ToDto(FilingRule r) => new(
        r.Id, r.WorkspaceId, r.Name, r.IsEnabled, r.Priority,
        r.VendorContains, r.Category, r.FileNameContains,
        r.AssignTagId, r.SetCategory, r.MoveToArchiveId,
        r.AppliedCount, r.CreatedAt);

    /// <summary>
    /// Pure match check: true when every condition the rule specifies matches the
    /// document. A rule with no conditions never matches (creation forbids it).
    /// Substring conditions are case-insensitive; the category condition is an
    /// exact match against the effective category (confirmed, else suggested).
    /// </summary>
    public static bool Matches(FilingRule rule, Document doc)
    {
        var hasCondition = false;
        if (rule.VendorContains is not null)
        {
            hasCondition = true;
            if (doc.ExtractedVendor is null ||
                !doc.ExtractedVendor.Contains(rule.VendorContains, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (rule.Category is not null)
        {
            hasCondition = true;
            var effective = doc.ConfirmedCategory ?? doc.SuggestedCategory;
            if (!string.Equals(effective, rule.Category, StringComparison.Ordinal))
                return false;
        }
        if (rule.FileNameContains is not null)
        {
            hasCondition = true;
            if (!doc.OriginalFileName.Contains(rule.FileNameContains, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return hasCondition;
    }

    public async Task<List<FilingRuleDto>> ListAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        return (await _rules.ListByWorkspaceAsync(workspaceId, ct)).Select(ToDto).ToList();
    }

    public async Task<FilingRuleDto> GetAsync(string userId, Guid workspaceId, Guid ruleId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var rule = await _rules.GetByIdAsync(workspaceId, ruleId, ct) ?? throw new NotFoundException("Filing rule not found.");
        return ToDto(rule);
    }

    public async Task<FilingRuleDto> CreateAsync(string userId, Guid workspaceId, CreateFilingRuleRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var rule = await ValidateAndBuildAsync(workspaceId, request.Name, request.IsEnabled, request.Priority,
            request.VendorContains, request.Category, request.FileNameContains,
            request.AssignTagId, request.SetCategory, request.MoveToArchiveId, ct);
        if (await _rules.GetByNameAsync(workspaceId, rule.Name, ct) is not null)
            throw new ValidationException($"A filing rule named '{rule.Name}' already exists in this workspace.");
        await _rules.AddAsync(rule, ct);
        await _audit.LogAsync(userId, workspaceId, "FilingRule.Created", "FilingRule", rule.Id.ToString(), rule.Name, ct);
        return ToDto(rule);
    }

    public async Task<FilingRuleDto> UpdateAsync(string userId, Guid workspaceId, Guid ruleId, UpdateFilingRuleRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var existing = await _rules.GetByIdAsync(workspaceId, ruleId, ct) ?? throw new NotFoundException("Filing rule not found.");
        var updated = await ValidateAndBuildAsync(workspaceId, request.Name, request.IsEnabled, request.Priority,
            request.VendorContains, request.Category, request.FileNameContains,
            request.AssignTagId, request.SetCategory, request.MoveToArchiveId, ct);
        if (!string.Equals(updated.Name, existing.Name, StringComparison.OrdinalIgnoreCase) &&
            await _rules.GetByNameAsync(workspaceId, updated.Name, ct) is not null)
            throw new ValidationException($"A filing rule named '{updated.Name}' already exists in this workspace.");
        existing.Name = updated.Name;
        existing.IsEnabled = updated.IsEnabled;
        existing.Priority = updated.Priority;
        existing.VendorContains = updated.VendorContains;
        existing.Category = updated.Category;
        existing.FileNameContains = updated.FileNameContains;
        existing.AssignTagId = updated.AssignTagId;
        existing.SetCategory = updated.SetCategory;
        existing.MoveToArchiveId = updated.MoveToArchiveId;
        await _rules.UpdateAsync(existing, ct);
        await _audit.LogAsync(userId, workspaceId, "FilingRule.Updated", "FilingRule", existing.Id.ToString(), existing.Name, ct);
        return ToDto(existing);
    }

    public async Task DeleteAsync(string userId, Guid workspaceId, Guid ruleId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var rule = await _rules.GetByIdAsync(workspaceId, ruleId, ct) ?? throw new NotFoundException("Filing rule not found.");
        await _rules.DeleteAsync(rule, ct);
        await _audit.LogAsync(userId, workspaceId, "FilingRule.Deleted", "FilingRule", rule.Id.ToString(), rule.Name, ct);
    }

    /// <summary>
    /// Validates a rule's shape and references and builds the entity. Shared by
    /// create and update so both reject the same bad input: empty/oversize name,
    /// no condition, no action, unknown category, or a tag/archive from another
    /// workspace (404, never a leak).
    /// </summary>
    private async Task<FilingRule> ValidateAndBuildAsync(Guid workspaceId, string name, bool isEnabled, int priority,
        string? vendorContains, string? category, string? fileNameContains,
        Guid? assignTagId, string? setCategory, Guid? moveToArchiveId, CancellationToken ct)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0) throw new ValidationException("Filing rule name is required.");
        if (trimmedName.Length > 100) throw new ValidationException("Filing rule name must be at most 100 characters.");
        if (priority < 0 || priority > 10000) throw new ValidationException("Priority must be between 0 and 10000.");

        var vendor = string.IsNullOrWhiteSpace(vendorContains) ? null : vendorContains.Trim();
        var fileName = string.IsNullOrWhiteSpace(fileNameContains) ? null : fileNameContains.Trim();
        var catCondition = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var catAction = string.IsNullOrWhiteSpace(setCategory) ? null : setCategory.Trim();
        if (vendor is not null && vendor.Length > 200) throw new ValidationException("Vendor match must be at most 200 characters.");
        if (fileName is not null && fileName.Length > 200) throw new ValidationException("File name match must be at most 200 characters.");
        if (catCondition is not null && !DocumentClassifier.IsKnownCategory(catCondition))
            throw new ValidationException($"Unknown category '{catCondition}'. Use one of: {string.Join(", ", DocumentClassifier.Categories)}.");
        if (catAction is not null && !DocumentClassifier.IsKnownCategory(catAction))
            throw new ValidationException($"Unknown category '{catAction}'. Use one of: {string.Join(", ", DocumentClassifier.Categories)}.");
        if (vendor is null && catCondition is null && fileName is null)
            throw new ValidationException("A filing rule needs at least one condition: vendor, category or file name.");
        if (assignTagId is null && catAction is null && moveToArchiveId is null)
            throw new ValidationException("A filing rule needs at least one action: assign a tag, set a category or move to an archive.");

        if (assignTagId is not null && await _tags.GetByIdAsync(workspaceId, assignTagId.Value, ct) is null)
            throw new NotFoundException("Tag not found.");
        if (moveToArchiveId is not null && await _archives.GetByIdAsync(workspaceId, moveToArchiveId.Value, ct) is null)
            throw new NotFoundException("Archive not found.");

        return new FilingRule
        {
            WorkspaceId = workspaceId,
            Name = trimmedName,
            IsEnabled = isEnabled,
            Priority = priority,
            VendorContains = vendor,
            Category = catCondition,
            FileNameContains = fileName,
            AssignTagId = assignTagId,
            SetCategory = catAction,
            MoveToArchiveId = moveToArchiveId
        };
    }

    /// <summary>
    /// Evaluates every enabled rule against a document (already loaded, with its
    /// OCR fields) and applies the matching ones, in Priority order. Returns the
    /// names of the rules that matched. All rules are matched against the
    /// document's fields as they were when evaluation started — an earlier rule's
    /// actions never change what a later rule matches; actions still apply in
    /// order, so for the single-value actions (set category, move to archive) the
    /// last matching rule wins. Persists the document changes, the tag assignments
    /// and each rule's AppliedCount. A stale tag/archive reference skips that
    /// action only and is noted in the audit details — one bad rule never blocks
    /// the others, and evaluation never throws (callers in the OCR pipeline must
    /// not fail a Ready document because a rule is stale).
    /// </summary>
    public async Task<List<string>> EvaluateAsync(string userId, Guid workspaceId, Document doc, CancellationToken ct = default)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        var rules = await _rules.ListByWorkspaceAsync(workspaceId, ct);
        var docChanged = false;

        // Snapshot: every rule matches against the same OCR result.
        var snapshot = new Document
        {
            WorkspaceId = doc.WorkspaceId,
            OriginalFileName = doc.OriginalFileName,
            StorageKey = doc.StorageKey,
            Sha256Hash = doc.Sha256Hash,
            MimeType = doc.MimeType,
            CreatedById = doc.CreatedById,
            ExtractedVendor = doc.ExtractedVendor,
            SuggestedCategory = doc.SuggestedCategory,
            ConfirmedCategory = doc.ConfirmedCategory
        };

        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            if (!Matches(rule, snapshot)) continue;
            applied.Add(rule.Name);
            rule.AppliedCount++;
            await _rules.UpdateAsync(rule, ct);

            if (rule.AssignTagId is not null)
            {
                var tag = await _tags.GetByIdAsync(workspaceId, rule.AssignTagId.Value, ct);
                if (tag is null)
                {
                    skipped.Add($"'{rule.Name}': tag no longer exists");
                }
                else if (await _tags.GetAssignmentAsync(workspaceId, doc.Id, tag.Id, ct) is null)
                {
                    await _tags.AddAssignmentAsync(new DocumentTag { DocumentId = doc.Id, TagId = tag.Id }, ct);
                }
            }
            if (rule.SetCategory is not null)
            {
                // A rule is a configured decision: the category counts as confirmed.
                doc.ConfirmedCategory = rule.SetCategory;
                doc.SuggestedCategory = rule.SetCategory;
                doc.NeedsReview = false;
                docChanged = true;
            }
            if (rule.MoveToArchiveId is not null)
            {
                var archive = await _archives.GetByIdAsync(workspaceId, rule.MoveToArchiveId.Value, ct);
                if (archive is null)
                {
                    skipped.Add($"'{rule.Name}': archive no longer exists");
                }
                else
                {
                    doc.ArchiveId = archive.Id;
                    docChanged = true;
                }
            }
        }

        if (docChanged)
            await _documents.UpdateAsync(doc, ct);

        if (applied.Count > 0)
        {
            var details = $"{doc.OriginalFileName}: filed by {string.Join(", ", applied.Select(n => $"'{n}'"))}";
            if (skipped.Count > 0) details += $" (skipped: {string.Join("; ", skipped)})";
            await _audit.LogAsync(userId, workspaceId, "Document.RuleFiled", "Document", doc.Id.ToString(), details, ct);
        }
        return applied;
    }

    /// <summary>
    /// Manual apply (StandardUser+): re-runs the workspace's enabled rules against
    /// one document — for example after creating a new rule, to file documents
    /// that were uploaded before it existed. Works on any document, even before
    /// OCR has run (file-name conditions can still match).
    /// </summary>
    public async Task<DocumentDto> ApplyToDocumentAsync(string userId, Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        await EvaluateAsync(userId, workspaceId, doc, ct);
        // Reload so the returned DTO includes tags assigned by the rules
        // (the in-memory navigation was loaded before evaluation ran).
        var reloaded = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        return DocumentDtos.ToDto(reloaded);
    }
}
