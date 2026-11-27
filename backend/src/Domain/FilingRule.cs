namespace DocuArchive.Domain;

/// <summary>
/// A filing rule (Phase 4 organisation): a user-configured "if this, then file it
/// that way" automation inside a workspace. When a document finishes OCR, every
/// enabled rule is checked against it in <see cref="Priority"/> order; every rule
/// whose conditions all match applies its actions. All rules match against the
/// same OCR result (an earlier rule's actions never change what a later rule
/// matches). Actions are additive (assign a tag), except the single-value actions
/// (set category, move to archive), where the last matching rule wins. A rule is a
/// user-configured decision, so a category it sets counts as confirmed (see
/// <see cref="Application.FilingRuleService"/>).
///
/// Conditions (all specified ones must match — AND):
/// <list type="bullet">
/// <item><see cref="VendorContains"/> — case-insensitive substring of the extracted vendor.</item>
/// <item><see cref="Category"/> — exact match against the document's effective category (confirmed, else suggested).</item>
/// <item><see cref="FileNameContains"/> — case-insensitive substring of the original file name.</item>
/// </list>
/// At least one condition and at least one action are required (enforced in the service).
/// Referenced tags/archives are nullable foreign keys with SetNull on delete: deleting
/// a tag or archive never deletes the rule — the stale action is simply skipped at
/// evaluation time and noted in the audit trail.
/// </summary>
public class FilingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string Name { get; set; }
    /// <summary>Disabled rules are listed but never evaluated.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Evaluation order (ascending, then CreatedAt). Lower runs first.</summary>
    public int Priority { get; set; }
    public string? VendorContains { get; set; }
    public string? Category { get; set; }
    public string? FileNameContains { get; set; }
    public Guid? AssignTagId { get; set; }
    public Tag? AssignTag { get; set; }
    public string? SetCategory { get; set; }
    public Guid? MoveToArchiveId { get; set; }
    public Archive? MoveToArchive { get; set; }
    /// <summary>How many documents this rule has matched (incremented at evaluation time).</summary>
    public int AppliedCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
