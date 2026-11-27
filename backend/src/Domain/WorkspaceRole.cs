namespace DocuArchive.Domain;

/// <summary>
/// Workspace-scoped roles. Stored per <see cref="WorkspaceMembership"/>, NOT as global
/// Identity roles (except <see cref="WorkspaceRole.SystemAdmin"/>, which is a global flag on the user).
/// Higher numeric rank = more privilege.
/// </summary>
public enum WorkspaceRole
{
    ReadOnly = 0,
    StandardUser = 1,
    ArchiveManager = 2,
    WorkspaceAdmin = 3,
    WorkspaceOwner = 4,
    /// <summary>
    /// Global platform administrator flag on the user. It does NOT grant workspace access:
    /// a system admin still needs an explicit membership in each workspace (see
    /// WorkspaceAccessService). Never assigned via workspace membership.
    /// </summary>
    SystemAdmin = 5
}

/// <summary>Role hierarchy helpers.</summary>
public static class WorkspaceRoleExtensions
{
    /// <summary>True when <paramref name="actual"/> grants at least the privilege of <paramref name="required"/>.</summary>
    public static bool AtLeast(this WorkspaceRole actual, WorkspaceRole required) => (int)actual >= (int)required;
}
