using System.Security.Cryptography;
using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>
/// Physical boxes (Phase 7 physical archive). Boxes are workspace-scoped homes
/// for paper originals: a name, a physical location ("Shelf B, row 3"), and a
/// short printed code. A document sits in at most one box at a time; moving it
/// just re-points the foreign key. All operations enforce workspace membership
/// explicitly; a box from another workspace is a 404, never a leak.
/// Creating, editing, deleting and assigning require StandardUser+.
/// Box changes and document moves are audited — where a paper original lives
/// is part of its filing history.
/// </summary>
public class PhysicalBoxService
{
    private readonly IPhysicalBoxRepository _boxes;
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceAccessService _access;
    private readonly IAuditService _audit;

    public PhysicalBoxService(IPhysicalBoxRepository boxes, IDocumentRepository documents, IWorkspaceAccessService access, IAuditService audit)
    {
        _boxes = boxes; _documents = documents; _access = access; _audit = audit;
    }

    /// <summary>Alphabet for printed box codes — no I, L, O or U, so a code read off a label cannot be misread.</summary>
    public static readonly char[] CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789".ToCharArray();

    /// <summary>Generates a label code like "BOX-7K2P9Q". Random (not derived from the box id) so codes cannot be guessed from one another.</summary>
    public static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        var chars = new char[6];
        for (var i = 0; i < 6; i++) chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return "BOX-" + new string(chars);
    }

    private async Task<PhysicalBoxDto> ToDtoAsync(PhysicalBox b, Guid workspaceId, CancellationToken ct)
        => new(b.Id, b.WorkspaceId, b.Name, b.Code, b.Location, b.Description,
            await _boxes.CountDocumentsAsync(workspaceId, b.Id, ct), b.CreatedAt);

    public async Task<List<PhysicalBoxDto>> ListAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var boxes = await _boxes.ListByWorkspaceAsync(workspaceId, ct);
        var result = new List<PhysicalBoxDto>(boxes.Count);
        foreach (var box in boxes) result.Add(await ToDtoAsync(box, workspaceId, ct));
        return result;
    }

    public async Task<PhysicalBoxDto> GetAsync(string userId, Guid workspaceId, Guid boxId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var box = await _boxes.GetByIdAsync(workspaceId, boxId, ct) ?? throw new NotFoundException("Physical box not found.");
        return await ToDtoAsync(box, workspaceId, ct);
    }

    /// <summary>Finds a box by its printed label code (the QR deep link resolves here). The code is globally unique, but the box is only returned when it lives in this workspace — a code from another workspace is a 404, never a leak.</summary>
    public async Task<PhysicalBoxDto> GetByCodeAsync(string userId, Guid workspaceId, string code, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var box = await _boxes.GetByCodeAsync(code, ct);
        if (box is null || box.WorkspaceId != workspaceId) throw new NotFoundException("Physical box not found.");
        return await ToDtoAsync(box, workspaceId, ct);
    }

    public async Task<List<DocumentDto>> ListDocumentsAsync(string userId, Guid workspaceId, Guid boxId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        _ = await _boxes.GetByIdAsync(workspaceId, boxId, ct) ?? throw new NotFoundException("Physical box not found.");
        return (await _documents.ListByPhysicalBoxAsync(workspaceId, boxId, ct)).Select(DocumentDtos.ToDto).ToList();
    }

    public async Task<PhysicalBoxDto> CreateAsync(string userId, Guid workspaceId, CreatePhysicalBoxRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var (name, location, description) = Validate(request.Name, request.Location, request.Description);
        if (await _boxes.GetByNameAsync(workspaceId, name, ct) is not null)
            throw new ValidationException($"A physical box named '{name}' already exists in this workspace.");
        // Codes are random; collisions are near-impossible, but the unique index
        // is the final guard — retry a few times rather than failing the request.
        // (A name duplicate is a 400 straight away — see the repository.)
        PhysicalBox? box = null;
        for (var attempt = 0; attempt < 5 && box is null; attempt++)
        {
            var candidate = new PhysicalBox { WorkspaceId = workspaceId, Name = name, Code = GenerateCode(), Location = location, Description = description };
            try
            {
                box = await _boxes.AddAsync(candidate, ct);
            }
            catch (DuplicatePhysicalBoxCodeException)
            {
                // Random code collided with an existing box — try another one.
            }
        }
        if (box is null) throw new ValidationException("Could not create the physical box. Please try again.");
        await _audit.LogAsync(userId, workspaceId, "PhysicalBox.Created", "PhysicalBox", box.Id.ToString(), $"{box.Name} ({box.Code}) — {box.Location}", ct);
        return await ToDtoAsync(box, workspaceId, ct);
    }

    public async Task<PhysicalBoxDto> UpdateAsync(string userId, Guid workspaceId, Guid boxId, UpdatePhysicalBoxRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var box = await _boxes.GetByIdAsync(workspaceId, boxId, ct) ?? throw new NotFoundException("Physical box not found.");
        var (name, location, description) = Validate(request.Name, request.Location, request.Description);
        if (await _boxes.GetByNameAsync(workspaceId, name, ct) is { Id: var existingId } && existingId != box.Id)
            throw new ValidationException($"A physical box named '{name}' already exists in this workspace.");
        box.Name = name;
        box.Location = location;
        box.Description = description;
        await _boxes.UpdateAsync(box, ct);
        await _audit.LogAsync(userId, workspaceId, "PhysicalBox.Updated", "PhysicalBox", box.Id.ToString(), $"{box.Name} ({box.Code}) — {box.Location}", ct);
        return await ToDtoAsync(box, workspaceId, ct);
    }

    public async Task DeleteAsync(string userId, Guid workspaceId, Guid boxId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var box = await _boxes.GetByIdAsync(workspaceId, boxId, ct) ?? throw new NotFoundException("Physical box not found.");
        await _boxes.DeleteAsync(box, ct);
        await _audit.LogAsync(userId, workspaceId, "PhysicalBox.Deleted", "PhysicalBox", box.Id.ToString(), $"{box.Name} ({box.Code})", ct);
    }

    /// <summary>Puts a document in a box. Idempotent — assigning the document to the box it already sits in returns the document unchanged, with no audit entry.</summary>
    public async Task<DocumentDto> AssignAsync(string userId, Guid workspaceId, Guid documentId, Guid boxId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        var box = await _boxes.GetByIdAsync(workspaceId, boxId, ct) ?? throw new NotFoundException("Physical box not found.");
        if (doc.PhysicalBoxId != boxId)
        {
            var previous = doc.PhysicalBox?.Name;
            doc.PhysicalBoxId = boxId;
            doc.PhysicalBox = box;
            await _documents.UpdateAsync(doc, ct);
            await _audit.LogAsync(userId, workspaceId, "Document.BoxAssigned", "Document", doc.Id.ToString(),
                previous is null
                    ? $"{doc.OriginalFileName}: filed in box '{box.Name}' ({box.Code})"
                    : $"{doc.OriginalFileName}: moved from box '{previous}' to '{box.Name}' ({box.Code})", ct);
        }
        return DocumentDtos.ToDto(doc);
    }

    /// <summary>Takes a document out of its box. Idempotent — a document in no box is a no-op.</summary>
    public async Task<DocumentDto> UnassignAsync(string userId, Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        if (doc.PhysicalBoxId is not null)
        {
            var previous = doc.PhysicalBox?.Name;
            doc.PhysicalBoxId = null;
            doc.PhysicalBox = null;
            await _documents.UpdateAsync(doc, ct);
            await _audit.LogAsync(userId, workspaceId, "Document.BoxUnassigned", "Document", doc.Id.ToString(),
                $"{doc.OriginalFileName}: removed from box '{previous}'", ct);
        }
        return DocumentDtos.ToDto(doc);
    }

    private static (string Name, string Location, string? Description) Validate(string name, string location, string? description)
    {
        name = name.Trim();
        location = location.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (name.Length == 0) throw new ValidationException("Box name is required.");
        if (name.Length > 100) throw new ValidationException("Box name must be at most 100 characters.");
        if (location.Length == 0) throw new ValidationException("Box location is required.");
        if (location.Length > 200) throw new ValidationException("Box location must be at most 200 characters.");
        if (description is { Length: > 1000 }) throw new ValidationException("Box description must be at most 1000 characters.");
        return (name, location, description);
    }
}
