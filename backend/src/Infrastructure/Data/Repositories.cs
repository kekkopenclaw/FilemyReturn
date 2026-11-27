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

    public Task<int> CountAllAsync(CancellationToken ct = default)
        => _db.Workspaces.CountAsync(ct);
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

public class EfFilingRuleRepository : IFilingRuleRepository
{
    private readonly AppDbContext _db;
    public EfFilingRuleRepository(AppDbContext db) => _db = db;

    public async Task<FilingRule> AddAsync(FilingRule rule, CancellationToken ct = default)
    {
        _db.FilingRules.Add(rule);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // RACE: two identical rule names passed the duplicate pre-check at the
            // same time and the unique index (WorkspaceId, Name) rejected the
            // second insert. That is a duplicate (400), not a server error —
            // translate it; anything else rethrows.
            var duplicate = await GetByNameAsync(rule.WorkspaceId, rule.Name, ct);
            if (duplicate is not null) throw new DuplicateFilingRuleException(rule.Name);
            throw;
        }
        return rule;
    }

    public Task<FilingRule?> GetByIdAsync(Guid workspaceId, Guid ruleId, CancellationToken ct = default)
        => _db.FilingRules.FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Id == ruleId, ct);

    public Task<FilingRule?> GetByNameAsync(Guid workspaceId, string name, CancellationToken ct = default)
    {
        var lowered = name.ToLowerInvariant();
        return _db.FilingRules.FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Name.ToLower() == lowered, ct);
    }

    public Task<List<FilingRule>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.FilingRules.Where(r => r.WorkspaceId == workspaceId)
            .OrderBy(r => r.Priority).ThenBy(r => r.CreatedAt).ToListAsync(ct);

    public async Task UpdateAsync(FilingRule rule, CancellationToken ct = default)
    {
        _db.FilingRules.Update(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(FilingRule rule, CancellationToken ct = default)
    {
        _db.FilingRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
    }
}

public class EfPhysicalBoxRepository : IPhysicalBoxRepository
{
    private readonly AppDbContext _db;
    public EfPhysicalBoxRepository(AppDbContext db) => _db = db;

    public async Task<PhysicalBox> AddAsync(PhysicalBox box, CancellationToken ct = default)
    {
        _db.PhysicalBoxes.Add(box);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // RACE: two identical box names (or two identical random codes)
            // passed the pre-check at the same time and the unique index
            // rejected the second insert. A name duplicate is a 400; a code
            // collision is retried by the service with a fresh code.
            if (await GetByNameAsync(box.WorkspaceId, box.Name, ct) is not null) throw new DuplicatePhysicalBoxException(box.Name);
            if (await GetByCodeAsync(box.Code, ct) is not null) throw new DuplicatePhysicalBoxCodeException();
            throw;
        }
        return box;
    }

    public Task<PhysicalBox?> GetByIdAsync(Guid workspaceId, Guid boxId, CancellationToken ct = default)
        => _db.PhysicalBoxes.FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId && b.Id == boxId, ct);

    public Task<PhysicalBox?> GetByNameAsync(Guid workspaceId, string name, CancellationToken ct = default)
    {
        var lowered = name.ToLowerInvariant();
        return _db.PhysicalBoxes.FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId && b.Name.ToLower() == lowered, ct);
    }

    public Task<PhysicalBox?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _db.PhysicalBoxes.FirstOrDefaultAsync(b => b.Code == code, ct);

    public Task<List<PhysicalBox>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.PhysicalBoxes.Where(b => b.WorkspaceId == workspaceId).OrderBy(b => b.Name).ToListAsync(ct);

    public Task<int> CountDocumentsAsync(Guid workspaceId, Guid boxId, CancellationToken ct = default)
        => _db.Documents.CountAsync(d => d.WorkspaceId == workspaceId && d.PhysicalBoxId == boxId, ct);

    public async Task UpdateAsync(PhysicalBox box, CancellationToken ct = default)
    {
        _db.PhysicalBoxes.Update(box);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // RACE: a same-named box landed between the check and the save.
            if (await GetByNameAsync(box.WorkspaceId, box.Name, ct) is { Id: var clashId } && clashId != box.Id)
                throw new DuplicatePhysicalBoxException(box.Name);
            throw;
        }
    }

    public async Task DeleteAsync(PhysicalBox box, CancellationToken ct = default)
    {
        _db.PhysicalBoxes.Remove(box);
        await _db.SaveChangesAsync(ct);
    }
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
            .Include(d => d.PhysicalBox)
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
            .Include(d => d.PhysicalBox)
            .Where(d => d.WorkspaceId == workspaceId);
        if (archiveId.HasValue) query = query.Where(d => d.ArchiveId == archiveId.Value);
        if (tagId.HasValue) query = query.Where(d => d.DocumentTags.Any(dt => dt.TagId == tagId.Value));
        if (needsReview.HasValue) query = query.Where(d => d.NeedsReview == needsReview.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = ApplyTextSearch(query, search.Trim(), IsPostgres());
        }
        return query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    /// <summary>True when this context talks to PostgreSQL (full-text search
    /// available); false for SQLite (LIKE matching).</summary>
    private bool IsPostgres()
        => _db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>Free-text match over file name, vendor and OCR text. On
    /// Postgres it is a real full-text query (<c>to_tsvector('english', file
    /// name + vendor + OCR text) @@ plainto_tsquery('english', q)</c>) —
    /// stemming and stop-words included, so "invoices" matches "invoice"
    /// and "the" matches nothing. On SQLite it is a LIKE match. The text is
    /// built with <c>?? ""</c> so a null OCR text or vendor cannot null the
    /// whole vector (Postgres <c>||</c> with NULL yields NULL). Public and
    /// static so tests can prove the Postgres SQL translation with
    /// <c>ToQueryString()</c> without a live Postgres server.</summary>
    public static IQueryable<Document> ApplyTextSearch(IQueryable<Document> query, string search, bool useFullTextSearch)
    {
        if (useFullTextSearch)
        {
            return query.Where(d => EF.Functions.ToTsVector("english",
                    (d.OriginalFileName ?? "") + " " + (d.ExtractedVendor ?? "") + " " + (d.OcrText ?? ""))
                .Matches(EF.Functions.PlainToTsQuery("english", search)));
        }
        // Escape LIKE wildcards so the user's text is matched literally.
        var term = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return query.Where(d =>
            EF.Functions.Like(d.OriginalFileName, term, "\\")
            || (d.OcrText != null && EF.Functions.Like(d.OcrText, term, "\\"))
            || (d.ExtractedVendor != null && EF.Functions.Like(d.ExtractedVendor, term, "\\")));
    }

    /// <summary>Phase 5 search. Filters are ANDed; the free-text match is
    /// provider-aware: on Postgres it is a real full-text query
    /// (<c>to_tsvector('english', file name + vendor + OCR text) @@
    /// plainto_tsquery('english', q)</c>) — stemming and stop-words included,
    /// so "invoices" matches "invoice" and "the" matches nothing. On SQLite
    /// it is the same LIKE match as the list endpoint. The text is built with
    /// <c>?? ""</c> so a null OCR text or vendor cannot null the whole vector
    /// (Postgres <c>||</c> with NULL yields NULL).</summary>
    public Task<List<Document>> SearchAsync(Guid workspaceId, DocumentSearchCriteria criteria, CancellationToken ct = default)
    {
        var query = _db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.PhysicalBox)
            .Where(d => d.WorkspaceId == workspaceId);
        if (criteria.ArchiveId.HasValue) query = query.Where(d => d.ArchiveId == criteria.ArchiveId.Value);
        if (criteria.TagId.HasValue) query = query.Where(d => d.DocumentTags.Any(dt => dt.TagId == criteria.TagId.Value));
        if (criteria.NeedsReview.HasValue) query = query.Where(d => d.NeedsReview == criteria.NeedsReview.Value);
        if (criteria.Status.HasValue) query = query.Where(d => d.Status == criteria.Status.Value);
        if (!string.IsNullOrWhiteSpace(criteria.Category))
        {
            var category = criteria.Category;
            query = query.Where(d => (d.ConfirmedCategory ?? d.SuggestedCategory) == category);
        }
        if (!string.IsNullOrWhiteSpace(criteria.Vendor))
        {
            // Escape LIKE wildcards so the user's text is matched literally.
            var term = "%" + criteria.Vendor.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(d => d.ExtractedVendor != null && EF.Functions.Like(d.ExtractedVendor, term, "\\"));
        }
        if (criteria.From.HasValue)
        {
            var from = criteria.From.Value.Date;
            query = query.Where(d => d.ExtractedDate >= from);
        }
        if (criteria.To.HasValue)
        {
            // `To` is an inclusive date: anything extracted on that day counts.
            var end = criteria.To.Value.Date.AddDays(1);
            query = query.Where(d => d.ExtractedDate < end);
        }
        if (!string.IsNullOrWhiteSpace(criteria.Query))
        {
            query = ApplyTextSearch(query, criteria.Query.Trim(), IsPostgres());
        }
        return query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => _db.Documents.CountAsync(d => d.WorkspaceId == workspaceId, ct);

    public Task<int> CountAllAsync(CancellationToken ct = default)
        => _db.Documents.CountAsync(ct);

    /// <summary>System-wide counts by document/OCR status plus the human-review queue depth (Phase 9 monitoring).</summary>
    public async Task<DocumentStatusCounts> CountByStatusAsync(CancellationToken ct = default)
    {
        var byStatus = await _db.Documents
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byOcr = await _db.Documents
            .GroupBy(d => d.OcrStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var needsReview = await _db.Documents.CountAsync(d => d.NeedsReview, ct);
        int Status(DocumentStatus s) => byStatus.Where(x => x.Status == s).Sum(x => x.Count);
        int Ocr(OcrStatus s) => byOcr.Where(x => x.Status == s).Sum(x => x.Count);
        return new DocumentStatusCounts(
            byStatus.Sum(x => x.Count),
            Status(DocumentStatus.Uploaded), Status(DocumentStatus.Processing),
            Status(DocumentStatus.Ready), Status(DocumentStatus.Failed),
            Ocr(OcrStatus.Queued), Ocr(OcrStatus.Processing), Ocr(OcrStatus.Failed),
            needsReview);
    }

    /// <summary>Documents sitting in one physical box (Phase 7), newest first.</summary>
    public Task<List<Document>> ListByPhysicalBoxAsync(Guid workspaceId, Guid boxId, CancellationToken ct = default)
        => _db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.PhysicalBox)
            .Where(d => d.WorkspaceId == workspaceId && d.PhysicalBoxId == boxId)
            .OrderByDescending(d => d.CreatedAt).ToListAsync(ct);

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

    public Task<int> CountByActionSinceAsync(string action, DateTime since, CancellationToken ct = default)
        => _db.AuditLogs.CountAsync(a => a.Action == action && a.CreatedAt >= since, ct);

    public Task<AuditLog?> GetLatestByActionAsync(string action, CancellationToken ct = default)
        => _db.AuditLogs.Where(a => a.Action == action)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
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
