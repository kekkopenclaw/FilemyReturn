namespace DocuArchive.Domain;

/// <summary>A workspace is the top-level isolation boundary. All data belongs to a workspace.</summary>
public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string CreatedById { get; set; }

    public ICollection<WorkspaceMembership> Memberships { get; set; } = new List<WorkspaceMembership>();
    public ICollection<Archive> Archives { get; set; } = new List<Archive>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<FilingRule> FilingRules { get; set; } = new List<FilingRule>();
    public ICollection<PhysicalBox> PhysicalBoxes { get; set; } = new List<PhysicalBox>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}

/// <summary>Membership of one user in one workspace, with a workspace-scoped role.</summary>
public class WorkspaceMembership
{
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string UserId { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.ReadOnly;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An archive (a collection / physical or logical grouping) inside a workspace.</summary>
public class Archive
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
