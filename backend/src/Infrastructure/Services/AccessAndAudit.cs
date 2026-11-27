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
/// Workspace access enforcement. There is NO blanket bypass: every user, including system
/// admins, needs an explicit <see cref="WorkspaceMembership"/> in the workspace to access
/// it. A system admin gets in the same way as anyone else — a workspace owner/admin grants
/// them membership (audited as "Workspace.MemberAdded"). A system admin who tries to open
/// a workspace they were never granted gets 404 (existence is not leaked) and the attempt
/// is written to the audit log as "Workspace.AccessDenied".
/// </summary>
public class WorkspaceAccessService : IWorkspaceAccessService
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IUserIdentityService _users;
    private readonly IAuditService _audit;

    public WorkspaceAccessService(IWorkspaceRepository workspaces, IUserIdentityService users, IAuditService audit)
    {
        _workspaces = workspaces; _users = users; _audit = audit;
    }

    public Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default)
        => _workspaces.GetMembershipAsync(workspaceId, userId, ct);

    public async Task<bool> IsMemberAsync(Guid workspaceId, string userId, CancellationToken ct = default)
        => await _workspaces.GetMembershipAsync(workspaceId, userId, ct) is not null;

    public async Task<bool> HasRoleAsync(Guid workspaceId, string userId, WorkspaceRole minimumRole, CancellationToken ct = default)
    {
        var membership = await _workspaces.GetMembershipAsync(workspaceId, userId, ct);
        return membership is not null && membership.Role.AtLeast(minimumRole);
    }

    public async Task<WorkspaceMembership> RequireMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default)
    {
        var membership = await _workspaces.GetMembershipAsync(workspaceId, userId, ct);
        if (membership is not null) return membership;

        // No membership: deny. When the caller is a system admin this is a cross-workspace
        // access attempt without an explicit grant — audit-log it. The 404 response does
        // not leak whether the workspace exists. Audit failures must never change the
        // access decision, so logging is best-effort.
        if (await IsSystemAdminAsync(userId, ct))
        {
            try
            {
                await _audit.LogAsync(userId, workspaceId, "Workspace.AccessDenied", "Workspace",
                    workspaceId.ToString(), "System admin attempted access without a workspace membership.", ct);
            }
            catch { /* best-effort audit only — the denial stands regardless */ }
        }
        throw new NotFoundException("Workspace not found.");
    }

    public async Task<WorkspaceMembership> RequireRoleAsync(Guid workspaceId, string userId, WorkspaceRole minimumRole, CancellationToken ct = default)
    {
        var membership = await RequireMembershipAsync(workspaceId, userId, ct);
        if (!membership.Role.AtLeast(minimumRole)) throw new ForbiddenException("Insufficient role for this operation.");
        return membership;
    }

    private async Task<bool> IsSystemAdminAsync(string userId, CancellationToken ct)
    {
        var profile = await _users.FindByIdAsync(userId, ct);
        return profile?.IsSystemAdmin ?? false;
    }
}
