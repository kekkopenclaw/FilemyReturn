using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>Workspace use cases. Every method enforces membership/role through <see cref="IWorkspaceAccessService"/> — isolation is explicit, never assumed.</summary>
public class WorkspaceService
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IUserIdentityService _users;
    private readonly IWorkspaceAccessService _access;
    private readonly IAuditService _audit;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentStorage _storage;

    public WorkspaceService(IWorkspaceRepository workspaces, IUserIdentityService users, IWorkspaceAccessService access, IAuditService audit,
        IDocumentRepository documents, IDocumentStorage storage)
    {
        _workspaces = workspaces; _users = users; _access = access; _audit = audit;
        _documents = documents; _storage = storage;
    }

    private static WorkspaceDto ToDto(Workspace w, string role) => new(w.Id, w.Name, w.Description, w.CreatedAt, w.CreatedById, role);

    /// <summary>Lists only the workspaces the user is a member of.</summary>
    public async Task<List<WorkspaceDto>> ListMineAsync(string userId, CancellationToken ct = default)
    {
        var list = await _workspaces.ListByUserAsync(userId, ct);
        var result = new List<WorkspaceDto>();
        foreach (var w in list)
        {
            var m = await _workspaces.GetMembershipAsync(w.Id, userId, ct);
            result.Add(ToDto(w, m?.Role.ToString() ?? WorkspaceRole.ReadOnly.ToString()));
        }
        return result;
    }

    /// <summary>Creates a workspace; the creator becomes its <see cref="WorkspaceRole.WorkspaceOwner"/>.</summary>
    public async Task<WorkspaceDto> CreateAsync(string userId, CreateWorkspaceRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ValidationException("Workspace name is required.");
        var workspace = new Workspace { Name = request.Name.Trim(), Description = request.Description?.Trim(), CreatedById = userId };
        await _workspaces.AddAsync(workspace, ct);
        await _workspaces.AddMembershipAsync(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id, UserId = userId, Role = WorkspaceRole.WorkspaceOwner
        }, ct);
        await _audit.LogAsync(userId, workspace.Id, "Workspace.Created", "Workspace", workspace.Id.ToString(), workspace.Name, ct);
        return ToDto(workspace, WorkspaceRole.WorkspaceOwner.ToString());
    }

    /// <summary>Gets one workspace. Non-members get 404 (existence is not leaked).</summary>
    public async Task<WorkspaceDto> GetAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        var membership = await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var workspace = await _workspaces.GetByIdAsync(workspaceId, ct) ?? throw new NotFoundException("Workspace not found.");
        return ToDto(workspace, membership.Role.ToString());
    }

    /// <summary>Lists workspace members (any member may view).</summary>
    public async Task<List<MemberDto>> ListMembersAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireMembershipAsync(workspaceId, userId, ct);
        var members = await _workspaces.ListMembersAsync(workspaceId, ct);
        var result = new List<MemberDto>();
        foreach (var m in members)
        {
            var profile = await _users.FindByIdAsync(m.UserId, ct);
            result.Add(new MemberDto(m.UserId, profile?.Email ?? string.Empty, profile?.DisplayName, m.Role.ToString(), m.JoinedAt));
        }
        return result;
    }

    /// <summary>Adds a member. WorkspaceOwner / WorkspaceAdmin only. The invited user must already have an account.</summary>
    public async Task<MemberDto> AddMemberAsync(string userId, Guid workspaceId, AddMemberRequest request, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.WorkspaceAdmin, ct);
        if (!Enum.TryParse<WorkspaceRole>(request.Role, ignoreCase: true, out var role)
            || role is WorkspaceRole.SystemAdmin or WorkspaceRole.WorkspaceOwner)
            throw new ValidationException("Role must be WorkspaceAdmin, ArchiveManager, StandardUser or ReadOnly.");
        var target = await _users.FindByEmailAsync(request.Email.Trim(), ct)
            ?? throw new NotFoundException("No user exists with that email.");
        var existing = await _workspaces.GetMembershipAsync(workspaceId, target.Id, ct);
        if (existing is not null) throw new ValidationException("User is already a member of this workspace.");
        await _workspaces.AddMembershipAsync(new WorkspaceMembership
        {
            WorkspaceId = workspaceId, UserId = target.Id, Role = role
        }, ct);
        await _audit.LogAsync(userId, workspaceId, "Workspace.MemberAdded", "WorkspaceMembership", target.Id, $"{target.Email} as {role}", ct);
        return new MemberDto(target.Id, target.Email, target.DisplayName, role.ToString(), DateTime.UtcNow);
    }

    /// <summary>
    /// Removes a member. WorkspaceOwner / WorkspaceAdmin only. The last WorkspaceOwner can
    /// never be removed (a workspace must always have an owner), and removing a WorkspaceOwner
    /// requires the caller to be a WorkspaceOwner too — an admin cannot evict an owner.
    /// Without this endpoint a compromised or rogue member could never be evicted.
    /// </summary>
    public async Task RemoveMemberAsync(string userId, Guid workspaceId, string targetUserId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.WorkspaceAdmin, ct);
        var membership = await _workspaces.GetMembershipAsync(workspaceId, targetUserId, ct)
            ?? throw new NotFoundException("User is not a member of this workspace.");
        if (membership.Role == WorkspaceRole.WorkspaceOwner)
        {
            var caller = await _workspaces.GetMembershipAsync(workspaceId, userId, ct);
            if (caller?.Role != WorkspaceRole.WorkspaceOwner)
                throw new ForbiddenException("Only a workspace owner can remove another owner.");
            if (await _workspaces.CountOwnersAsync(workspaceId, ct) <= 1)
                throw new ValidationException("The last workspace owner cannot be removed.");
        }
        await _workspaces.RemoveMembershipAsync(workspaceId, targetUserId, ct);
        var target = await _users.FindByIdAsync(targetUserId, ct);
        await _audit.LogAsync(userId, workspaceId, "Workspace.MemberRemoved", "WorkspaceMembership", targetUserId,
            $"{target?.Email ?? targetUserId} removed from workspace", ct);
    }

    /// <summary>
    /// Changes a member's role. WorkspaceOwner / WorkspaceAdmin only. The WorkspaceOwner role
    /// can never be granted or taken away here (ownership transfer is a separate, deliberate
    /// action); the last owner's role can never be lowered. Removing an owner's role requires
    /// the caller to be an owner.
    /// </summary>
    public async Task<MemberDto> ChangeMemberRoleAsync(string userId, Guid workspaceId, string targetUserId, string role, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.WorkspaceAdmin, ct);
        if (!Enum.TryParse<WorkspaceRole>(role, ignoreCase: true, out var newRole)
            || newRole is WorkspaceRole.SystemAdmin or WorkspaceRole.WorkspaceOwner)
            throw new ValidationException("Role must be WorkspaceAdmin, ArchiveManager, StandardUser or ReadOnly.");
        var membership = await _workspaces.GetMembershipAsync(workspaceId, targetUserId, ct)
            ?? throw new NotFoundException("User is not a member of this workspace.");
        if (membership.Role == WorkspaceRole.WorkspaceOwner)
        {
            var caller = await _workspaces.GetMembershipAsync(workspaceId, userId, ct);
            if (caller?.Role != WorkspaceRole.WorkspaceOwner)
                throw new ForbiddenException("Only a workspace owner can change another owner's role.");
            if (await _workspaces.CountOwnersAsync(workspaceId, ct) <= 1)
                throw new ValidationException("The last workspace owner's role cannot be changed.");
        }
        var previous = membership.Role;
        membership.Role = newRole;
        await _workspaces.UpdateMembershipAsync(membership, ct);
        var target = await _users.FindByIdAsync(targetUserId, ct);
        await _audit.LogAsync(userId, workspaceId, "Workspace.MemberRoleChanged", "WorkspaceMembership", targetUserId,
            $"{target?.Email ?? targetUserId}: role {previous} → {newRole}", ct);
        return new MemberDto(targetUserId, target?.Email ?? string.Empty, target?.DisplayName, newRole.ToString(), membership.JoinedAt);
    }

    /// <summary>
    /// Deletes a workspace. WorkspaceOwner only. The deletion is audited FIRST (the audit row
    /// survives the delete — its workspace link is set to null — so the deletion itself stays
    /// in the trail), every stored file of the workspace's documents is deleted from storage
    /// (deleted data must not linger on disk), and only then is the workspace row removed.
    /// </summary>
    public async Task DeleteAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.WorkspaceOwner, ct);
        var workspace = await _workspaces.GetByIdAsync(workspaceId, ct) ?? throw new NotFoundException("Workspace not found.");
        await _audit.LogAsync(userId, workspaceId, "Workspace.Deleted", "Workspace", workspaceId.ToString(), workspace.Name, ct);
        var docs = await _documents.ListByWorkspaceAsync(workspaceId, null, ct);
        foreach (var doc in docs)
        {
            try { await _storage.DeleteAsync(doc.StorageKey, ct); }
            catch { /* a single unreadable file must not block the delete; the row is still removed */ }
        }
        await _workspaces.DeleteAsync(workspace, ct);
    }
}
