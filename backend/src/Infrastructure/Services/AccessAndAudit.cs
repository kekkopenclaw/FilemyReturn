using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;

namespace DocuArchive.Infrastructure.Services;

/// <summary>Append-only audit writer. There is intentionally no update/delete path.</summary>
public class AuditService : IAuditService
{
    private readonly IAuditRepository _repo;
    public AuditService(IAuditRepository repo) => _repo = repo;

    /// <inheritdoc/>
    public Task LogAsync(string userId, Guid? workspaceId, string action, string? entityType = null, string? entityId = null, string? details = null, CancellationToken ct = default)
        => _repo.AddAsync(new AuditLog
        {
            UserId = userId, WorkspaceId = workspaceId, Action = action,
            EntityType = entityType, EntityId = entityId, Details = details
        }, ct);
}

/// <summary>
/// Workspace access enforcement. System admins bypass membership checks; everyone else
/// must be a member, and role checks use the hierarchy in <see cref="WorkspaceRoleExtensions"/>.
/// </summary>
public class WorkspaceAccessService : IWorkspaceAccessService
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IUserIdentityService _users;

    public WorkspaceAccessService(IWorkspaceRepository workspaces, IUserIdentityService users)
    {
        _workspaces = workspaces; _users = users;
    }

    public Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default)
        => _workspaces.GetMembershipAsync(workspaceId, userId, ct);

    public async Task<bool> IsMemberAsync(Guid workspaceId, string userId, CancellationToken ct = default)
    {
        if (await IsSystemAdminAsync(userId, ct)) return true;
        return await _workspaces.GetMembershipAsync(workspaceId, userId, ct) is not null;
    }

    public async Task<bool> HasRoleAsync(Guid workspaceId, string userId, WorkspaceRole minimumRole, CancellationToken ct = default)
    {
        if (await IsSystemAdminAsync(userId, ct)) return true;
        var membership = await _workspaces.GetMembershipAsync(workspaceId, userId, ct);
        return membership is not null && membership.Role.AtLeast(minimumRole);
    }

    public async Task<WorkspaceMembership> RequireMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default)
    {
        if (await IsSystemAdminAsync(userId, ct))
        {
            var existing = await _workspaces.GetMembershipAsync(workspaceId, userId, ct);
            if (existing is not null) return existing;
            // System admin without a membership: grant owner-level access without creating a membership row.
            return new WorkspaceMembership { WorkspaceId = workspaceId, UserId = userId, Role = WorkspaceRole.SystemAdmin };
        }
        return await _workspaces.GetMembershipAsync(workspaceId, userId, ct)
            ?? throw new NotFoundException("Workspace not found.");
    }

    public async Task<WorkspaceMembership> RequireRoleAsync(Guid workspaceId, string userId, WorkspaceRole minimumRole, CancellationToken ct = default)
    {
        var membership = await RequireMembershipAsync(workspaceId, userId, ct);
        if (membership.Role is WorkspaceRole.SystemAdmin) return membership;
        if (!membership.Role.AtLeast(minimumRole)) throw new ForbiddenException("Insufficient role for this operation.");
        return membership;
    }

    private async Task<bool> IsSystemAdminAsync(string userId, CancellationToken ct)
    {
        var profile = await _users.FindByIdAsync(userId, ct);
        return profile?.IsSystemAdmin ?? false;
    }
}
