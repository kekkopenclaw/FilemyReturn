namespace DocuArchive.Domain;

/// <summary>
/// A tag (label) inside a workspace. Tags are free-form organisation: a document
/// can carry many tags, a tag can mark many documents (Phase 4 organisation).
/// Names are unique per workspace (case-insensitive — enforced in the service;
/// the database index guards exact duplicates and races).
/// </summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string Name { get; set; }
    /// <summary>Display colour as #RRGGBB, or null (the service assigns a deterministic palette colour).</summary>
    public string? Colour { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
}

/// <summary>Join row: one tag assigned to one document. Composite key — assigning the same tag twice is impossible by construction.</summary>
public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }
    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
