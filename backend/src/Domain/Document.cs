namespace DocuArchive.Domain;

/// <summary>Processing status of a document.</summary>
public enum DocumentStatus
{
    Uploaded,
    Processing,
    Ready,
    Failed
}

/// <summary>OCR status of a document. Phase 1: no upload endpoint exists yet, so this stays <see cref="OcrStatus.NotStarted"/>.</summary>
public enum OcrStatus
{
    NotStarted,
    Queued,
    Processing,
    Completed,
    Failed
}

/// <summary>
/// Document metadata skeleton (Phase 1). No upload endpoint exists in Phase 1, so rows are
/// only created by later phases. OCR fields stay null / NotStarted — never invented.
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid? ArchiveId { get; set; }
    public Archive? Archive { get; set; }

    public required string OriginalFileName { get; set; }
    /// <summary>Opaque storage key (never a user-supplied path). See <see cref="IDocumentStorage"/>.</summary>
    public required string StorageKey { get; set; }
    /// <summary>SHA-256 hex of the file bytes.</summary>
    public required string Sha256Hash { get; set; }
    public long FileSizeBytes { get; set; }
    public required string MimeType { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public OcrStatus OcrStatus { get; set; } = OcrStatus.NotStarted;
    /// <summary>Extracted text. Null until Phase 3 OCR runs. Never invented.</summary>
    public string? OcrText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string CreatedById { get; set; }
}

/// <summary>Append-only audit record. There is no API endpoint that edits or deletes audit logs.</summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Null for account-level events (e.g. login) that belong to no workspace.</summary>
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string UserId { get; set; }
    /// <summary>e.g. "Auth.Login", "Workspace.Created", "Archive.Created".</summary>
    public required string Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Hashed refresh token. Only the SHA-256 hash is stored; the plaintext token is shown to the client once.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    /// <summary>SHA-256 hex of the plaintext token.</summary>
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
