using DocuArchive.Domain;
using DocuArchive.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace DocuArchive.Infrastructure.Identity;

/// <summary>
/// Identity-backed implementation of <see cref="IUserIdentityService"/>.
/// Password hashing, lockout counting (5 failures / 15 min, per Identity LockoutOptions)
/// and password policy are all handled by ASP.NET Core Identity here — Application code
/// never touches UserManager / SignInManager directly.
/// </summary>
public class IdentityUserService : IUserIdentityService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;

    public IdentityUserService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        _users = users; _signIn = signIn;
    }

    private static UserProfile ToProfile(ApplicationUser u) =>
        new(u.Id, u.Email ?? string.Empty, u.DisplayName, u.IsSystemAdmin, u.CreatedAt, u.MustChangePassword);

    public async Task<UserProfile?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        return user is null ? null : ToProfile(user);
    }

    public async Task<UserProfile?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        return user is null ? null : ToProfile(user);
    }

    public async Task<(bool Succeeded, string[] Errors, UserProfile? User)> CreateUserAsync(string email, string password, string? displayName, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim()
        };
        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded) return (false, result.Errors.Select(e => e.Description).ToArray(), null);
        return (true, [], ToProfile(user));
    }

    public async Task<SignInOutcome> CheckPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null) return SignInOutcome.NotFound;
        var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut) return SignInOutcome.LockedOut;
        return result.Succeeded ? SignInOutcome.Success : SignInOutcome.InvalidCredentials;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return false;
        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded) return false;
        // A successful password change clears the first-login requirement.
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await _users.UpdateAsync(user);
        }
        return true;
    }
}
