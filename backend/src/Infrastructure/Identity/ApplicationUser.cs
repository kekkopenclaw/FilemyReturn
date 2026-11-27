using Microsoft.AspNetCore.Identity;

namespace DocuArchive.Infrastructure.Identity;

/// <summary>
/// Identity user (Infrastructure). This is the ONLY user type that inherits IdentityUser;
/// Application/Domain code only sees the plain <see cref="Domain.UserProfile"/>.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Display name shown in the UI.</summary>
    public string? DisplayName { get; set; }

    /// <summary>UTC time the account was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True for the global platform administrator. Workspace roles are per-workspace.</summary>
    public bool IsSystemAdmin { get; set; }

    /// <summary>
    /// True when the user must change their password before they can log in
    /// (e.g. the seeded admin). Cleared automatically on a successful password change.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
