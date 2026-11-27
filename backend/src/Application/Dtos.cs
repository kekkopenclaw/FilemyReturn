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

/// <summary>
/// Refresh-token request. The token usually travels in the
/// <c>docuarchive.refresh</c> HttpOnly cookie (see AuthController); the body
/// field is a fallback for non-browser API clients that hold a token. Either
/// way, the token is never returned in a response body.
/// </summary>
public record RefreshRequest(string? RefreshToken);

/// <summary>Logout request — same cookie-or-body token as <see cref="RefreshRequest"/>.</summary>
public record LogoutRequest(string? RefreshToken);

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

/// <summary>Change a member's role (WorkspaceAdmin, ArchiveManager, StandardUser or ReadOnly).</summary>
public record ChangeMemberRoleRequest([Required] string Role);

public record CreateArchiveRequest(
    [Required, MinLength(1), MaxLength(200)] string Name,
    [MaxLength(1000)] string? Description);

public record ArchiveDto(Guid Id, Guid WorkspaceId, string Name, string? Description, DateTime CreatedAt);

/// <summary>A workspace tag (Phase 4 organisation). Colour is always set (explicit or palette default).</summary>
public record TagDto(Guid Id, Guid WorkspaceId, string Name, string Colour, DateTime CreatedAt);

public record CreateTagRequest(
    [Required, MinLength(1), MaxLength(64)] string Name,
    string? Colour);

/// <summary>Assign a tag to a document. Body: { "tagId": "…" }.</summary>
public record AssignTagRequest([Required] Guid TagId);

/// <summary>
/// A filing rule (Phase 4 organisation). Conditions are all optional individually,
/// but at least one condition and at least one action are required (enforced by
/// the service). Category/SetCategory must be one of the classifier categories.
/// </summary>
public record FilingRuleDto(
    Guid Id, Guid WorkspaceId, string Name, bool IsEnabled, int Priority,
    string? VendorContains, string? Category, string? FileNameContains,
    Guid? AssignTagId, string? SetCategory, Guid? MoveToArchiveId,
    int AppliedCount, DateTime CreatedAt);

public record CreateFilingRuleRequest(
    [Required, MinLength(1), MaxLength(100)] string Name,
    bool IsEnabled = true,
    [Range(0, 10000)] int Priority = 0,
    [MaxLength(200)] string? VendorContains = null,
    [MaxLength(64)] string? Category = null,
    [MaxLength(200)] string? FileNameContains = null,
    Guid? AssignTagId = null,
    [MaxLength(64)] string? SetCategory = null,
    Guid? MoveToArchiveId = null);

public record UpdateFilingRuleRequest(
    [Required, MinLength(1), MaxLength(100)] string Name,
    bool IsEnabled = true,
    [Range(0, 10000)] int Priority = 0,
    [MaxLength(200)] string? VendorContains = null,
    [MaxLength(64)] string? Category = null,
    [MaxLength(200)] string? FileNameContains = null,
    Guid? AssignTagId = null,
    [MaxLength(64)] string? SetCategory = null,
    Guid? MoveToArchiveId = null);

public record DocumentDto(
    Guid Id, Guid WorkspaceId, Guid? ArchiveId, string OriginalFileName,
    string Sha256Hash, long FileSizeBytes, string MimeType,
    string Status, string OcrStatus, DateTime CreatedAt, string CreatedById,
    // Phase 3 OCR / receipt extraction — all null until OCR runs (backward compatible: optional).
    string? OcrText = null, double? OcrConfidence = null,
    string? ExtractedVendor = null, DateTime? ExtractedDate = null,
    decimal? ExtractedTotal = null, string? ExtractedCurrency = null,
    string? SuggestedCategory = null,
    // Phase 3 classification — null/false until OCR + classification run.
    double? ClassificationConfidence = null, string? ClassificationReason = null,
    bool NeedsReview = false, string? ConfirmedCategory = null,
    // Phase 4 organisation — tags assigned to this document (empty when none).
    List<TagDto>? Tags = null);

public record DashboardDto(
    Guid WorkspaceId, int ArchiveCount, int DocumentCount, int MemberCount,
    int DocumentsReady, int DocumentsProcessing, int DocumentsFailed, int OcrPending,
    int AwaitingReview = 0);

/// <summary>Confirm or correct a document's category (human review queue).</summary>
public record ConfirmCategoryRequest([Required] string Category);

/// <summary>
/// Edit a document's extracted metadata (Phase 4). Full-form PUT semantics:
/// the client always sends every field; null clears vendor, date or total.
/// Category null clears the human-confirmed category (the suggestion stays);
/// a known category counts as confirmed, exactly like ConfirmCategory.
/// File name is always sent — a document always has one.
/// </summary>
public record UpdateDocumentMetadataRequest(
    [Required, MinLength(1), MaxLength(255)] string OriginalFileName,
    [MaxLength(200)] string? ExtractedVendor,
    DateTime? ExtractedDate,
    decimal? ExtractedTotal,
    [MaxLength(64)] string? Category);

/// <summary>One entry of a workspace's upload history (Phase 2).</summary>
public record UploadHistoryDto(
    Guid DocumentId, Guid WorkspaceId, Guid? ArchiveId, string OriginalFileName,
    long FileSizeBytes, string Sha256Hash, string MimeType,
    string Status, string OcrStatus, string UploadedById, string? UploadedByEmail,
    DateTime UploadedAt);

public record AuditLogDto(
    Guid Id, Guid? WorkspaceId, string UserId, string Action,
    string? EntityType, string? EntityId, string? Details, DateTime CreatedAt);
