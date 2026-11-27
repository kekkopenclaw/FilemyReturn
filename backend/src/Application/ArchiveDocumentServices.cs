using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>Archive, document, dashboard and audit-log read use cases. All are workspace-scoped and enforce membership explicitly.</summary>
public class ArchiveService
{
    private readonly IArchiveRepository _archives;
    private readonly IWorkspaceAccessService _access;
    private readonly IAuditService _audit;

    public ArchiveService(IArchiveRepository archives, IWorkspaceAccessService access, IAuditService audit)
    {
        _archives = archives; _access = access; _audit = audit;
    }

    private static ArchiveDto ToDto(Archive a) => new(a.Id, a.WorkspaceId, a.Name, a.Description, a.CreatedAt);

    public async Task<List<ArchiveDto>> ListAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        return (await _archives.ListByWorkspaceAsync(workspaceId, ct)).Select(ToDto).ToList();
    }

    /// <summary>Creates an archive. ReadOnly members cannot create (requires StandardUser+).</summary>
    public async Task<ArchiveDto> CreateAsync(string userId, Guid workspaceId, CreateArchiveRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ValidationException("Archive name is required.");
        var archive = new Archive { WorkspaceId = workspaceId, Name = request.Name.Trim(), Description = request.Description?.Trim() };
        await _archives.AddAsync(archive, ct);
        await _audit.LogAsync(userId, workspaceId, "Archive.Created", "Archive", archive.Id.ToString(), archive.Name, ct);
        return ToDto(archive);
    }

    public async Task<ArchiveDto> GetAsync(string userId, Guid workspaceId, Guid archiveId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var archive = await _archives.GetByIdAsync(workspaceId, archiveId, ct) ?? throw new NotFoundException("Archive not found.");
        return ToDto(archive);
    }
}

/// <summary>Document reads + secure download + upload history (Phase 2 adds download/history; upload lives in <see cref="DocumentUploadService"/>).</summary>
public class DocumentService
{
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceAccessService _access;
    private readonly IDocumentStorage _storage;
    private readonly IUserIdentityService _users;
    private readonly IAuditService _audit;

    public DocumentService(IDocumentRepository documents, IWorkspaceAccessService access, IDocumentStorage storage, IUserIdentityService users, IAuditService audit)
    {
        _documents = documents; _access = access; _storage = storage; _users = users; _audit = audit;
    }

    private static DocumentDto ToDto(Document d) => DocumentDtos.ToDto(d);

    /// <summary>Lists a workspace's documents, newest first. Optional <paramref name="search"/>
    /// matches file name, extracted vendor or the full OCR text (Phase 3 full-text search).
    /// Optional <paramref name="needsReview"/> restricts to the human-review queue.</summary>
    public async Task<List<DocumentDto>> ListAsync(string userId, Guid workspaceId, Guid? archiveId, CancellationToken ct = default, string? search = null, bool? needsReview = null)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        return (await _documents.ListByWorkspaceAsync(workspaceId, archiveId, ct, search, needsReview)).Select(ToDto).ToList();
    }

    /// <summary>
    /// Human review: confirms or corrects a document's category (Phase 3 classification).
    /// StandardUser+ only. The confirmed category overrides the suggestion and clears the
    /// review flag; the previous suggestion stays visible in the audit log. 400 on an
    /// unknown category, 404 on a missing document.
    /// </summary>
    public async Task<DocumentDto> ConfirmCategoryAsync(string userId, Guid workspaceId, Guid documentId, string category, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        if (!DocumentClassifier.IsKnownCategory(category))
            throw new ValidationException($"Unknown category '{category}'. Use one of: {string.Join(", ", DocumentClassifier.Categories)}.");
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        var previous = doc.ConfirmedCategory ?? doc.SuggestedCategory ?? "—";
        doc.ConfirmedCategory = category;
        doc.SuggestedCategory = category;
        doc.NeedsReview = false;
        await _documents.UpdateAsync(doc, ct);
        await _audit.LogAsync(userId, workspaceId, "Document.CategoryConfirmed", "Document", doc.Id.ToString(),
            $"{doc.OriginalFileName}: category {previous} → {category}", ct);
        return ToDto(doc);
    }

    public async Task<DocumentDto> GetAsync(string userId, Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        return ToDto(doc);
    }

    /// <summary>Opens a document for secure download (members only). Downloads are audited — a document leaving the system must leave a trail.</summary>
    public async Task<(DocumentDto Document, Stream Content)> GetDownloadAsync(string userId, Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        var stream = await _storage.OpenReadAsync(doc.StorageKey, ct) ?? throw new NotFoundException("Stored file not found.");
        await _audit.LogAsync(userId, workspaceId, "Document.Downloaded", "Document", doc.Id.ToString(), doc.OriginalFileName, ct);
        return (ToDto(doc), stream);
    }

    /// <summary>Upload history: every document uploaded to this workspace, newest first, with the uploader.</summary>
    public async Task<List<UploadHistoryDto>> ListUploadHistoryAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var docs = await _documents.ListByWorkspaceAsync(workspaceId, null, ct);
        var result = new List<UploadHistoryDto>();
        foreach (var d in docs)
        {
            var uploader = await _users.FindByIdAsync(d.CreatedById, ct);
            result.Add(new UploadHistoryDto(
                d.Id, d.WorkspaceId, d.ArchiveId, d.OriginalFileName, d.FileSizeBytes,
                d.Sha256Hash, d.MimeType, d.Status.ToString(), d.OcrStatus.ToString(),
                d.CreatedById, uploader?.Email, d.CreatedAt));
        }
        return result;
    }
}

/// <summary>Dashboard counts, computed from the real database (0 when empty — no mock data).</summary>
public class DashboardService
{
    private readonly IArchiveRepository _archives;
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IWorkspaceAccessService _access;

    public DashboardService(IArchiveRepository archives, IDocumentRepository documents, IWorkspaceRepository workspaces, IWorkspaceAccessService access)
    {
        _archives = archives; _documents = documents; _workspaces = workspaces; _access = access;
    }

    public async Task<DashboardDto> GetAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var docs = await _documents.ListByWorkspaceAsync(workspaceId, null, ct);
        return new DashboardDto(
            workspaceId,
            await _archives.CountByWorkspaceAsync(workspaceId, ct),
            docs.Count,
            await _workspaces.CountMembersAsync(workspaceId, ct),
            docs.Count(d => d.Status == DocumentStatus.Ready),
            docs.Count(d => d.Status == DocumentStatus.Processing),
            docs.Count(d => d.Status == DocumentStatus.Failed),
            docs.Count(d => d.OcrStatus == OcrStatus.NotStarted || d.OcrStatus == OcrStatus.Queued),
            docs.Count(d => d.NeedsReview));
    }
}

/// <summary>Audit-log reads. ArchiveManager+ only; read-only by design — no edit/delete method exists.</summary>
public class AuditQueryService
{
    private readonly IAuditRepository _auditRepo;
    private readonly IWorkspaceAccessService _access;

    public AuditQueryService(IAuditRepository auditRepo, IWorkspaceAccessService access)
    {
        _auditRepo = auditRepo; _access = access;
    }

    public async Task<List<AuditLogDto>> ListAsync(string userId, Guid workspaceId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.ArchiveManager, ct);
        // Clamp both bounds: a negative skip throws inside EF Core (500), and take is capped at 100.
        var logs = await _auditRepo.ListByWorkspaceAsync(workspaceId, Math.Max(0, skip), Math.Clamp(take, 1, 100), ct);
        return logs.Select(l => new AuditLogDto(l.Id, l.WorkspaceId, l.UserId, l.Action, l.EntityType, l.EntityId, l.Details, l.CreatedAt)).ToList();
    }
}
