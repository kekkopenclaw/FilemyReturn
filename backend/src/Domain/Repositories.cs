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

/// <summary>Persistence for documents (always scoped to a workspace). Phase 2 adds upload (create) support.</summary>
public interface IDocumentRepository
{
    Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid workspaceId, Guid documentId, CancellationToken cancellationToken = default);
    /// <summary>Finds a document in this workspace with the given SHA-256 hash (duplicate detection).</summary>
    Task<Document?> GetByHashAsync(Guid workspaceId, string sha256Hash, CancellationToken cancellationToken = default);
    Task<List<Document>> ListByWorkspaceAsync(Guid workspaceId, Guid? archiveId, CancellationToken cancellationToken = default);
    Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
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
}
