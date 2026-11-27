using System.Text.RegularExpressions;
using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>
/// Tags (Phase 4 organisation). Tags are workspace-scoped labels: a document can
/// carry many tags, a tag can mark many documents. All operations enforce workspace
/// membership explicitly; a tag from another workspace is a 404, never a leak.
/// Creating/deleting tags requires StandardUser+; assigning and unassigning too.
/// Tagging is audited — a label is part of a document's filing history.
/// </summary>
public class TagService
{
    private readonly ITagRepository _tags;
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceAccessService _access;
    private readonly IAuditService _audit;

    public TagService(ITagRepository tags, IDocumentRepository documents, IWorkspaceAccessService access, IAuditService audit)
    {
        _tags = tags; _documents = documents; _access = access; _audit = audit;
    }

    /// <summary>Palette for tags created without an explicit colour — deterministic by name, so the same name always gets the same colour.</summary>
    public static readonly string[] DefaultColours = ["#0e7c66", "#2563eb", "#b7790a", "#b42318", "#6d28d9", "#0e7490", "#be185d", "#4d7c0f"];

    public static string DefaultColour(string name)
    {
        var hash = 0;
        foreach (var c in name) hash = (hash * 31 + c) % int.MaxValue;
        return DefaultColours[Math.Abs(hash) % DefaultColours.Length];
    }

    /// <summary>Null or a #RRGGBB hex colour; anything else is rejected with 400.</summary>
    public static bool IsValidColour(string? colour)
        => colour is null || Regex.IsMatch(colour, "^#[0-9a-fA-F]{6}$");

    private static TagDto ToDto(Tag t) => new(t.Id, t.WorkspaceId, t.Name, t.Colour ?? DefaultColour(t.Name), t.CreatedAt);

    public async Task<List<TagDto>> ListAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        return (await _tags.ListByWorkspaceAsync(workspaceId, ct)).Select(ToDto).ToList();
    }

    public async Task<TagDto> CreateAsync(string userId, Guid workspaceId, CreateTagRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var name = request.Name.Trim();
        if (name.Length == 0) throw new ValidationException("Tag name is required.");
        if (name.Length > 64) throw new ValidationException("Tag name must be at most 64 characters.");
        if (!IsValidColour(request.Colour)) throw new ValidationException("Colour must be a #RRGGBB hex value, e.g. #0e7c66.");
        if (await _tags.GetByNameAsync(workspaceId, name, ct) is not null)
            throw new ValidationException($"A tag named '{name}' already exists in this workspace.");
        var tag = new Tag { WorkspaceId = workspaceId, Name = name, Colour = request.Colour ?? DefaultColour(name) };
        await _tags.AddAsync(tag, ct);
        await _audit.LogAsync(userId, workspaceId, "Tag.Created", "Tag", tag.Id.ToString(), tag.Name, ct);
        return ToDto(tag);
    }

    public async Task DeleteAsync(string userId, Guid workspaceId, Guid tagId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var tag = await _tags.GetByIdAsync(workspaceId, tagId, ct) ?? throw new NotFoundException("Tag not found.");
        await _tags.DeleteAsync(tag, ct);
        await _audit.LogAsync(userId, workspaceId, "Tag.Deleted", "Tag", tag.Id.ToString(), tag.Name, ct);
    }

    /// <summary>Assigns a tag to a document. Idempotent — assigning a tag that is already there returns the current tag list.</summary>
    public async Task<List<TagDto>> AssignAsync(string userId, Guid workspaceId, Guid documentId, Guid tagId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        var tag = await _tags.GetByIdAsync(workspaceId, tagId, ct) ?? throw new NotFoundException("Tag not found.");
        if (await _tags.GetAssignmentAsync(workspaceId, documentId, tagId, ct) is null)
        {
            await _tags.AddAssignmentAsync(new DocumentTag { DocumentId = documentId, TagId = tagId }, ct);
            await _audit.LogAsync(userId, workspaceId, "Document.Tagged", "Document", doc.Id.ToString(),
                $"{doc.OriginalFileName}: tagged '{tag.Name}'", ct);
        }
        return await ListDocumentTagsAsync(userId, workspaceId, documentId, ct);
    }

    /// <summary>Removes a tag from a document. Idempotent — removing a tag that is not there is a no-op.</summary>
    public async Task<List<TagDto>> UnassignAsync(string userId, Guid workspaceId, Guid documentId, Guid tagId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        var tag = await _tags.GetByIdAsync(workspaceId, tagId, ct) ?? throw new NotFoundException("Tag not found.");
        var assignment = await _tags.GetAssignmentAsync(workspaceId, documentId, tagId, ct);
        if (assignment is not null)
        {
            await _tags.RemoveAssignmentAsync(assignment, ct);
            await _audit.LogAsync(userId, workspaceId, "Document.Untagged", "Document", doc.Id.ToString(),
                $"{doc.OriginalFileName}: untagged '{tag.Name}'", ct);
        }
        return await ListDocumentTagsAsync(userId, workspaceId, documentId, ct);
    }

    public async Task<List<TagDto>> ListDocumentTagsAsync(string userId, Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        _ = await _documents.GetByIdAsync(workspaceId, documentId, ct) ?? throw new NotFoundException("Document not found.");
        return (await _tags.ListTagsForDocumentAsync(workspaceId, documentId, ct)).Select(ToDto).ToList();
    }
}
