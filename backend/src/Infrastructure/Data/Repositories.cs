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

public class EfDocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _db;
    public EfDocumentRepository(AppDbContext db) => _db = db;

    public async Task<Document> AddAsync(Document document, CancellationToken ct = default)
    {
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);
        return document;
    }

    public Task<Document?> GetByIdAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => _db.Documents.FirstOrDefaultAsync(d => d.WorkspaceId == workspaceId && d.Id == documentId, ct);

    public Task<Document?> GetByHashAsync(Guid workspaceId, string sha256Hash, CancellationToken ct = default)
        => _db.Documents.FirstOrDefaultAsync(d => d.WorkspaceId == workspaceId && d.Sha256Hash == sha256Hash, ct);

    public Task<List<Document>> ListByWorkspaceAsync(Guid workspaceId, Guid? archiveId, CancellationToken ct = default)
    {
        var query = _db.Documents.Where(d => d.WorkspaceId == workspaceId);
        if (archiveId.HasValue) query = query.Where(d => d.ArchiveId == archiveId.Value);
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
}
