using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>Workspace use cases. Every method enforces membership/role through <see cref="IWorkspaceAccessService"/> — isolation is explicit, never assumed.</summary>
public class WorkspaceService
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IUserIdentityService _users;
    private readonly IWorkspaceAccessService _access;
    private readonly IAuditService _audit;

    public WorkspaceService(IWorkspaceRepository workspaces, IUserIdentityService users, IWorkspaceAccessService access, IAuditService audit)
    {
        _workspaces = workspaces; _users = users; _access = access; _audit = audit;
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

    /// <summary>Deletes a workspace. WorkspaceOwner only.</summary>
    public async Task DeleteAsync(string userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.WorkspaceOwner, ct);
        var workspace = await _workspaces.GetByIdAsync(workspaceId, ct) ?? throw new NotFoundException("Workspace not found.");
        await _workspaces.DeleteAsync(workspace, ct);
        await _audit.LogAsync(userId, workspaceId, "Workspace.Deleted", "Workspace", workspaceId.ToString(), workspace.Name, ct);
    }
}
