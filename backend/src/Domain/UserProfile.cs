namespace DocuArchive.Domain;

/// <summary>
/// Plain user profile in the Domain layer.
///
/// EXCEPTION (documented): ASP.NET Core Identity's user type (<c>ApplicationUser :
/// IdentityUser</c>) lives in Infrastructure, because Identity is an infrastructure
/// concern and Domain must not reference ASP.NET Core / EF Core. Application services
/// only ever see this plain profile; the Identity user is never exposed above
/// Infrastructure.
/// </summary>
public record UserProfile(
    string Id,
    string Email,
    string? DisplayName,
    bool IsSystemAdmin,
    DateTime CreatedAt,
    bool MustChangePassword = false);
