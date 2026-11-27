namespace DocuArchive.Domain;

/// <summary>Result of OCR text extraction. Phase 3 (PaddleOCR) fills this in; Phase 1 never fabricates one.</summary>
public record OcrResult(string? Text, double? Confidence, string? Language, bool Success);

/// <summary>OCR engine abstraction. Implemented by PaddleOCR in Phase 3.</summary>
public interface IOcrEngine
{
    /// <summary>Extracts text from the document stored under <paramref name="storageKey"/>.</summary>
    Task<OcrResult> ExtractTextAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>Document file storage abstraction. Phase 1: local filesystem only (see Infrastructure). No S3 implementation yet (Phase 2 TODO).</summary>
public interface IDocumentStorage
{
    /// <summary>Stores <paramref name="content"/> and returns an opaque storage key (a random GUID filename, never the user-supplied name).</summary>
    Task<string> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken cancellationToken = default);
    /// <summary>Opens a stored file for reading, or null when the key does not exist.</summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    /// <summary>Deletes a stored file. No-op when the key does not exist.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>Append-only audit writer. Audit records are never editable or deletable via the API.</summary>
public interface IAuditService
{
    /// <summary>Writes one audit record. <paramref name="workspaceId"/> is null for account-level events (e.g. login).</summary>
    Task LogAsync(string userId, Guid? workspaceId, string action, string? entityType = null, string? entityId = null, string? details = null, CancellationToken cancellationToken = default);
}

/// <summary>Workspace membership / role checks. Every workspace endpoint must go through this — no global query filters are used, isolation is enforced here, explicitly.</summary>
public interface IWorkspaceAccessService
{
    Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
    /// <summary>True when the user is a member with at least <paramref name="minimumRole"/> (or is a system admin).</summary>
    Task<bool> HasRoleAsync(Guid workspaceId, string userId, WorkspaceRole minimumRole, CancellationToken cancellationToken = default);
    /// <summary>Returns the membership or throws a not-found error when the user is not a member (404, so workspace existence is not leaked).</summary>
    Task<WorkspaceMembership> RequireMembershipAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default);
    /// <summary>Requires membership plus at least <paramref name="minimumRole"/>; throws NotFound (non-member) or Forbidden (insufficient role).</summary>
    Task<WorkspaceMembership> RequireRoleAsync(Guid workspaceId, string userId, WorkspaceRole minimumRole, CancellationToken cancellationToken = default);
}

/// <summary>JWT access-token creation plus refresh-token generation/hashing. Implemented in Application (pure token logic, no Identity dependency).</summary>
public interface IJwtService
{
    /// <summary>Creates a short-lived (15 min) signed access token for <paramref name="user"/>.</summary>
    string CreateAccessToken(UserProfile user);
    /// <summary>Generates a new opaque refresh token (plaintext — only its hash is stored).</summary>
    string CreateRefreshToken();
    /// <summary>SHA-256 hex hash of a refresh token, for storage/comparison.</summary>
    string HashToken(string token);
    /// <summary>Access-token lifetime.</summary>
    TimeSpan AccessTokenLifetime { get; }
    /// <summary>Refresh-token lifetime.</summary>
    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>Outcome of a password check (lockout is enforced by the Identity implementation).</summary>
public enum SignInOutcome { Success, InvalidCredentials, LockedOut, NotFound }

/// <summary>
/// Identity operations behind an interface so Application never references ASP.NET Core
/// Identity. Implemented in Infrastructure with UserManager / SignInManager.
/// </summary>
public interface IUserIdentityService
{
    Task<UserProfile?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<UserProfile?> FindByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string[] Errors, UserProfile? User)> CreateUserAsync(string email, string password, string? displayName, CancellationToken cancellationToken = default);
    Task<SignInOutcome> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

/// <summary>Email abstraction. Phase 1 implementation only logs (no SMTP account, no paid service).</summary>
public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>One OCR job for the Phase 3 worker queue.</summary>
public record OcrJob(Guid DocumentId, Guid WorkspaceId, string StorageKey);

/// <summary>Channel-based OCR job queue (Phase 3 worker). Phase 1: registration + interface only, no processing.</summary>
public interface IOcrJobQueue
{
    ValueTask EnqueueAsync(OcrJob job, CancellationToken cancellationToken = default);
    Task<OcrJob?> DequeueAsync(CancellationToken cancellationToken = default);
}
