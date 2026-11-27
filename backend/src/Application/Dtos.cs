using System.ComponentModel.DataAnnotations;

namespace DocuArchive.Application;

/// <summary>
/// REST DTOs — the exact Phase 1 API contract shared by WebApi controllers and the
/// frontend. Routes / shapes must not change without coordinating with the frontend agent.
/// </summary>
public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(12)] string Password,
    string? DisplayName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record RefreshRequest([Required] string RefreshToken);

public record LogoutRequest([Required] string RefreshToken);

public record ChangePasswordRequest(
    string? Email,
    [Required] string CurrentPassword,
    [Required, MinLength(12)] string NewPassword);

public record UserDto(string Id, string Email, string? DisplayName, bool IsSystemAdmin, DateTime CreatedAt, bool MustChangePassword = false);

public record AuthResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, UserDto User);

public record CreateWorkspaceRequest(
    [Required, MinLength(1), MaxLength(200)] string Name,
    [MaxLength(1000)] string? Description);

public record WorkspaceDto(Guid Id, string Name, string? Description, DateTime CreatedAt, string CreatedById, string Role);

public record MemberDto(string UserId, string Email, string? DisplayName, string Role, DateTime JoinedAt);

public record AddMemberRequest(
    [Required, EmailAddress] string Email,
    [Required] string Role);

public record CreateArchiveRequest(
    [Required, MinLength(1), MaxLength(200)] string Name,
    [MaxLength(1000)] string? Description);

public record ArchiveDto(Guid Id, Guid WorkspaceId, string Name, string? Description, DateTime CreatedAt);

public record DocumentDto(
    Guid Id, Guid WorkspaceId, Guid? ArchiveId, string OriginalFileName,
    string Sha256Hash, long FileSizeBytes, string MimeType,
    string Status, string OcrStatus, DateTime CreatedAt, string CreatedById);

public record DashboardDto(
    Guid WorkspaceId, int ArchiveCount, int DocumentCount, int MemberCount,
    int DocumentsReady, int DocumentsProcessing, int DocumentsFailed, int OcrPending);

/// <summary>One entry of a workspace's upload history (Phase 2).</summary>
public record UploadHistoryDto(
    Guid DocumentId, Guid WorkspaceId, Guid? ArchiveId, string OriginalFileName,
    long FileSizeBytes, string Sha256Hash, string MimeType,
    string Status, string OcrStatus, string UploadedById, string? UploadedByEmail,
    DateTime UploadedAt);

public record AuditLogDto(
    Guid Id, Guid? WorkspaceId, string UserId, string Action,
    string? EntityType, string? EntityId, string? Details, DateTime CreatedAt);
