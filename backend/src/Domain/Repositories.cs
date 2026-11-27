namespace DocuArchive.Domain;

/// <summary>Persistence for workspaces and their memberships.</summary>
public interface IWorkspaceRepository
{
    Task<Workspace> AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task<Workspace?> GetByIdAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<List<Workspace>> ListByUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
    Task<List<WorkspaceMembership>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddMembershipAsync(WorkspaceMembership membership, CancellationToken cancellationToken = default);
    /// <summary>Removes a membership (member removal). No-op when the membership does not exist.</summary>
    Task RemoveMembershipAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
    /// <summary>Changes a member's role (persisted via the tracked membership).</summary>
    Task UpdateMembershipAsync(WorkspaceMembership membership, CancellationToken cancellationToken = default);
    /// <summary>Counts members holding <see cref="WorkspaceRole.WorkspaceOwner"/> — used to protect the last owner.</summary>
    Task<int> CountOwnersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task<int> CountMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for archives (always scoped to a workspace).</summary>
public interface IArchiveRepository
{
    Task<Archive> AddAsync(Archive archive, CancellationToken cancellationToken = default);
    Task<Archive?> GetByIdAsync(Guid workspaceId, Guid archiveId, CancellationToken cancellationToken = default);
    Task<List<Archive>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for tags (always scoped to a workspace) and their document assignments.</summary>
public interface ITagRepository
{
    Task<Tag> AddAsync(Tag tag, CancellationToken cancellationToken = default);
    Task<Tag?> GetByIdAsync(Guid workspaceId, Guid tagId, CancellationToken cancellationToken = default);
    /// <summary>Case-insensitive name lookup (for duplicate detection).</summary>
    Task<Tag?> GetByNameAsync(Guid workspaceId, string name, CancellationToken cancellationToken = default);
    Task<List<Tag>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Tag tag, CancellationToken cancellationToken = default);
    Task<DocumentTag?> GetAssignmentAsync(Guid workspaceId, Guid documentId, Guid tagId, CancellationToken cancellationToken = default);
    Task AddAssignmentAsync(DocumentTag assignment, CancellationToken cancellationToken = default);
    Task RemoveAssignmentAsync(DocumentTag assignment, CancellationToken cancellationToken = default);
    /// <summary>Tags assigned to one document, in tag-name order.</summary>
    Task<List<Tag>> ListTagsForDocumentAsync(Guid workspaceId, Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for physical boxes (always scoped to a workspace) and their document contents.</summary>
public interface IPhysicalBoxRepository
{
    Task<PhysicalBox> AddAsync(PhysicalBox box, CancellationToken cancellationToken = default);
    Task<PhysicalBox?> GetByIdAsync(Guid workspaceId, Guid boxId, CancellationToken cancellationToken = default);
    /// <summary>Case-insensitive name lookup (for duplicate detection).</summary>
    Task<PhysicalBox?> GetByNameAsync(Guid workspaceId, string name, CancellationToken cancellationToken = default);
    /// <summary>Lookup by the printed label code (globally unique).</summary>
    Task<PhysicalBox?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    /// <summary>All boxes in the workspace, in name order.</summary>
    Task<List<PhysicalBox>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    /// <summary>How many documents sit in this box (for the list view).</summary>
    Task<int> CountDocumentsAsync(Guid workspaceId, Guid boxId, CancellationToken cancellationToken = default);
    Task UpdateAsync(PhysicalBox box, CancellationToken cancellationToken = default);
    Task DeleteAsync(PhysicalBox box, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for filing rules (always scoped to a workspace).</summary>
public interface IFilingRuleRepository
{
    Task<FilingRule> AddAsync(FilingRule rule, CancellationToken cancellationToken = default);
    Task<FilingRule?> GetByIdAsync(Guid workspaceId, Guid ruleId, CancellationToken cancellationToken = default);
    /// <summary>Case-insensitive name lookup (for duplicate detection).</summary>
    Task<FilingRule?> GetByNameAsync(Guid workspaceId, string name, CancellationToken cancellationToken = default);
    /// <summary>All rules in the workspace, in evaluation order (Priority, then CreatedAt).</summary>
    Task<List<FilingRule>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task UpdateAsync(FilingRule rule, CancellationToken cancellationToken = default);
    Task DeleteAsync(FilingRule rule, CancellationToken cancellationToken = default);
}

/// <summary>Phase 5 search criteria. Every field is optional, but a search needs
/// at least one of them (the service enforces that). <c>Category</c> matches the
/// effective category (human-confirmed wins over the suggestion). <c>From</c> /
/// <c>To</c> are inclusive dates on the extracted document date.</summary>
public record DocumentSearchCriteria(
    string? Query,
    string? Category,
    string? Vendor,
    DateTime? From,
    DateTime? To,
    Guid? TagId,
    Guid? ArchiveId,
    bool? NeedsReview,
    DocumentStatus? Status);

/// <summary>Persistence for documents (always scoped to a workspace). Phase 2 adds upload (create) support; Phase 3 adds OCR claim/update.</summary>
public interface IDocumentRepository
{
    Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid workspaceId, Guid documentId, CancellationToken cancellationToken = default);
    /// <summary>Finds a document in this workspace with the given SHA-256 hash (duplicate detection).</summary>
    Task<Document?> GetByHashAsync(Guid workspaceId, string sha256Hash, CancellationToken cancellationToken = default);
    Task<List<Document>> ListByWorkspaceAsync(Guid workspaceId, Guid? archiveId, CancellationToken cancellationToken = default, string? search = null, bool? needsReview = null, Guid? tagId = null);
    /// <summary>Phase 5 search: free-text match plus filters, newest first. The
    /// free-text match is provider-aware — PostgreSQL full-text search
    /// (<c>to_tsvector</c> / <c>plainto_tsquery</c>) when the provider is Postgres,
    /// LIKE matching on SQLite.</summary>
    Task<List<Document>> SearchAsync(Guid workspaceId, DocumentSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    /// <summary>Documents sitting in one physical box (Phase 7), newest first. Tags are loaded with them, like the list endpoint.</summary>
    Task<List<Document>> ListByPhysicalBoxAsync(Guid workspaceId, Guid boxId, CancellationToken cancellationToken = default);
    /// <summary>Persists changes to an already-tracked document (status, OCR text, extracted fields).</summary>
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    /// <summary>
    /// Atomically claims a queued document for OCR: sets Status=Processing, OcrStatus=Processing
    /// only when OcrStatus is currently Queued. Returns false when another worker already claimed it
    /// (or it is no longer queued) — the caller must then skip it, never process it twice.
    /// </summary>
    Task<bool> TryClaimForOcrAsync(Guid workspaceId, Guid documentId, CancellationToken cancellationToken = default);
    /// <summary>Documents waiting for OCR (OcrStatus Queued), oldest first, capped at <paramref name="take"/>.</summary>
    Task<List<Document>> ListQueuedForOcrAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for audit logs (append-only; no update/delete methods exist by design).</summary>
public interface IAuditRepository
{
    Task AddAsync(AuditLog entry, CancellationToken cancellationToken = default);
    Task<List<AuditLog>> ListByWorkspaceAsync(Guid workspaceId, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<int> CountByUserAsync(string userId, string action, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for hashed refresh tokens.</summary>
public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeAsync(RefreshToken token, CancellationToken cancellationToken = default);
    /// <summary>
    /// Revokes every refresh token of a user (password change, or a stolen token presented
    /// again — refresh-token reuse). Revocation is soft (RevokedAt is set); rows are kept
    /// so a later reuse of any of them is still detectable.
    /// </summary>
    Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);
}
