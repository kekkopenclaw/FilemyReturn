using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DocuArchive.Infrastructure.Data;

/// <summary>EF Core implementations of the Domain repository interfaces.</summary>
public class EfWorkspaceRepository : IWorkspaceRepository
{
    private readonly AppDbContext _db;
    public EfWorkspaceRepository(AppDbContext db) => _db = db;

    public async Task<Workspace> AddAsync(Workspace workspace, CancellationToken ct = default)
    {
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync(ct);
        return workspace;
    }

    public Task<Workspace?> GetByIdAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);

    public Task<List<Workspace>> ListByUserAsync(string userId, CancellationToken ct = default)
        => _db.Workspaces.Where(w => w.Memberships.Any(m => m.UserId == userId)).OrderBy(w => w.Name).ToListAsync(ct);

    public Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default)
        => _db.WorkspaceMemberships.FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);

    public Task<List<WorkspaceMembership>> ListMembersAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.WorkspaceMemberships.Where(m => m.WorkspaceId == workspaceId).OrderBy(m => m.JoinedAt).ToListAsync(ct);

    public async Task AddMembershipAsync(WorkspaceMembership membership, CancellationToken ct = default)
    {
        _db.WorkspaceMemberships.Add(membership);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default)
    {
        var membership = await GetMembershipAsync(workspaceId, userId, ct);
        if (membership is null) return;
        _db.WorkspaceMemberships.Remove(membership);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateMembershipAsync(WorkspaceMembership membership, CancellationToken ct = default)
    {
        _db.WorkspaceMemberships.Update(membership);
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> CountOwnersAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.WorkspaceMemberships.CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.WorkspaceOwner, ct);

    public async Task DeleteAsync(Workspace workspace, CancellationToken ct = default)
    {
        _db.Workspaces.Remove(workspace);
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> CountMembersAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.WorkspaceMemberships.CountAsync(m => m.WorkspaceId == workspaceId, ct);
}

public class EfArchiveRepository : IArchiveRepository
{
    private readonly AppDbContext _db;
    public EfArchiveRepository(AppDbContext db) => _db = db;

    public async Task<Archive> AddAsync(Archive archive, CancellationToken ct = default)
    {
        _db.Archives.Add(archive);
        await _db.SaveChangesAsync(ct);
        return archive;
    }

    public Task<Archive?> GetByIdAsync(Guid workspaceId, Guid archiveId, CancellationToken ct = default)
        => _db.Archives.FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.Id == archiveId, ct);

    public Task<List<Archive>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.Archives.Where(a => a.WorkspaceId == workspaceId).OrderBy(a => a.Name).ToListAsync(ct);

    public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.Archives.CountAsync(a => a.WorkspaceId == workspaceId, ct);
}

public class EfTagRepository : ITagRepository
{
    private readonly AppDbContext _db;
    public EfTagRepository(AppDbContext db) => _db = db;

    public async Task<Tag> AddAsync(Tag tag, CancellationToken ct = default)
    {
        _db.Tags.Add(tag);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // RACE: two identical tag names passed the duplicate pre-check at the same
            // time and the unique index (WorkspaceId, Name) rejected the second insert.
            // That is a duplicate (400), not a server error — translate it; anything else rethrows.
            var duplicate = await GetByNameAsync(tag.WorkspaceId, tag.Name, ct);
            if (duplicate is not null) throw new DuplicateTagException(tag.Name);
            throw;
        }
        return tag;
    }

    public Task<Tag?> GetByIdAsync(Guid workspaceId, Guid tagId, CancellationToken ct = default)
        => _db.Tags.FirstOrDefaultAsync(t => t.WorkspaceId == workspaceId && t.Id == tagId, ct);

    public Task<Tag?> GetByNameAsync(Guid workspaceId, string name, CancellationToken ct = default)
    {
        var lowered = name.ToLowerInvariant();
        return _db.Tags.FirstOrDefaultAsync(t => t.WorkspaceId == workspaceId && t.Name.ToLower() == lowered, ct);
    }

    public Task<List<Tag>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.Tags.Where(t => t.WorkspaceId == workspaceId).OrderBy(t => t.Name).ToListAsync(ct);

    public async Task DeleteAsync(Tag tag, CancellationToken ct = default)
    {
        _db.Tags.Remove(tag);
        await _db.SaveChangesAsync(ct);
    }

    public Task<DocumentTag?> GetAssignmentAsync(Guid workspaceId, Guid documentId, Guid tagId, CancellationToken ct = default)
        => _db.DocumentTags.FirstOrDefaultAsync(dt => dt.DocumentId == documentId && dt.TagId == tagId
            && dt.Tag != null && dt.Tag.WorkspaceId == workspaceId, ct);

    public async Task AddAssignmentAsync(DocumentTag assignment, CancellationToken ct = default)
    {
        _db.DocumentTags.Add(assignment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAssignmentAsync(DocumentTag assignment, CancellationToken ct = default)
    {
        _db.DocumentTags.Remove(assignment);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<Tag>> ListTagsForDocumentAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => _db.DocumentTags.Where(dt => dt.DocumentId == documentId && dt.Tag != null && dt.Tag.WorkspaceId == workspaceId)
            .Select(dt => dt.Tag!)
            .OrderBy(t => t.Name).ToListAsync(ct);
}

public class EfDocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _db;
    public EfDocumentRepository(AppDbContext db) => _db = db;

    public async Task<Document> AddAsync(Document document, CancellationToken ct = default)
    {
        _db.Documents.Add(document);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // RACE: two identical uploads passed the duplicate pre-check at the same time and
            // the unique index (WorkspaceId, Sha256Hash) rejected the second insert. That is a
            // duplicate (409), not a server error — translate it; anything else rethrows.
            var duplicate = await GetByHashAsync(document.WorkspaceId, document.Sha256Hash, ct);
            if (duplicate is not null) throw new DuplicateDocumentException(duplicate.Id, duplicate.OriginalFileName);
            throw;
        }
        return document;
    }

    public Task<Document?> GetByIdAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => _db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .FirstOrDefaultAsync(d => d.WorkspaceId == workspaceId && d.Id == documentId, ct);

    public Task<Document?> GetByHashAsync(Guid workspaceId, string sha256Hash, CancellationToken ct = default)
        => _db.Documents.FirstOrDefaultAsync(d => d.WorkspaceId == workspaceId && d.Sha256Hash == sha256Hash, ct);

    /// <summary>
    /// Lists a workspace's documents, newest first. When <paramref name="search"/> is given,
    /// matches file name, extracted vendor or the full OCR text (Phase 3 full-text search).
    /// The match runs in the database (EF.Functions.Like — case-insensitive for ASCII in
    /// SQLite, and in PostgreSQL with a case-insensitive collation) and is always scoped
    /// to this workspace, so a search can never leak another workspace's documents.
    /// When <paramref name="tagId"/> is given, only documents carrying that tag are returned.
    /// Tags are always loaded with the documents (one join) so the DTO can list them.
    /// </summary>
    public Task<List<Document>> ListByWorkspaceAsync(Guid workspaceId, Guid? archiveId, CancellationToken ct = default, string? search = null, bool? needsReview = null, Guid? tagId = null)
    {
        var query = _db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Where(d => d.WorkspaceId == workspaceId);
        if (archiveId.HasValue) query = query.Where(d => d.ArchiveId == archiveId.Value);
        if (tagId.HasValue) query = query.Where(d => d.DocumentTags.Any(dt => dt.TagId == tagId.Value));
        if (needsReview.HasValue) query = query.Where(d => d.NeedsReview == needsReview.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escape LIKE wildcards so the user's text is matched literally.
            var term = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(d =>
                EF.Functions.Like(d.OriginalFileName, term, "\\")
                || (d.OcrText != null && EF.Functions.Like(d.OcrText, term, "\\"))
                || (d.ExtractedVendor != null && EF.Functions.Like(d.ExtractedVendor, term, "\\")));
        }
        return query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.Documents.CountAsync(d => d.WorkspaceId == workspaceId, ct);

    public async Task UpdateAsync(Document document, CancellationToken ct = default)
    {
        _db.Documents.Update(document);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryClaimForOcrAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        // Atomic claim: only a row still Queued flips to Processing. 0 rows = already claimed.
        var affected = await _db.Documents
            .Where(d => d.WorkspaceId == workspaceId && d.Id == documentId && d.OcrStatus == OcrStatus.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, DocumentStatus.Processing)
                .SetProperty(d => d.OcrStatus, OcrStatus.Processing), ct);
        return affected == 1;
    }

    public Task<List<Document>> ListQueuedForOcrAsync(int take, CancellationToken ct = default)
        => _db.Documents.Where(d => d.OcrStatus == OcrStatus.Queued)
            .OrderBy(d => d.CreatedAt).Take(take).ToListAsync(ct);
}

public class EfAuditRepository : IAuditRepository
{
    private readonly AppDbContext _db;
    public EfAuditRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AuditLog entry, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<AuditLog>> ListByWorkspaceAsync(Guid workspaceId, int skip, int take, CancellationToken ct = default)
        => _db.AuditLogs.Where(a => a.WorkspaceId == workspaceId)
            .OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

    public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.AuditLogs.CountAsync(a => a.WorkspaceId == workspaceId, ct);

    public Task<int> CountByUserAsync(string userId, string action, CancellationToken ct = default)
        => _db.AuditLogs.CountAsync(a => a.UserId == userId && a.Action == action, ct);
}

public class EfRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    public EfRefreshTokenRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public async Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.RevokedAt, now), ct);
    }
}
