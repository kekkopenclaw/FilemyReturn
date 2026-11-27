using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>Authentication use cases: register, login (with Identity lockout), refresh-token rotation, logout, change password. Audit-logs login/register events.</summary>
public class AuthService
{
    private readonly IUserIdentityService _users;
    private readonly IJwtService _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditService _audit;

    public AuthService(IUserIdentityService users, IJwtService jwt, IRefreshTokenRepository refreshTokens, IAuditService audit)
    {
        _users = users; _jwt = jwt; _refreshTokens = refreshTokens; _audit = audit;
    }

    private static UserDto ToDto(UserProfile u) => new(u.Id, u.Email, u.DisplayName, u.IsSystemAdmin, u.CreatedAt, u.MustChangePassword);

    private async Task<AuthResponse> IssueTokensAsync(UserProfile user, CancellationToken ct)
    {
        var access = _jwt.CreateAccessToken(user);
        var refresh = _jwt.CreateRefreshToken();
        await _refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _jwt.HashToken(refresh),
            ExpiresAt = DateTime.UtcNow.Add(_jwt.RefreshTokenLifetime)
        }, ct);
        return new AuthResponse(access, refresh, DateTime.UtcNow.Add(_jwt.AccessTokenLifetime), ToDto(user));
    }

    /// <summary>Registers a new user. Throws <see cref="ValidationException"/> when the email is taken or the password fails policy.</summary>
    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) throw new ValidationException("Email is required.");
        var (succeeded, errors, user) = await _users.CreateUserAsync(request.Email.Trim(), request.Password, request.DisplayName, ct);
        if (!succeeded || user is null) throw new ValidationException(errors.Length > 0 ? errors : ["Registration failed."]);
        await _audit.LogAsync(user.Id, null, "Auth.Registered", "User", user.Id, null, ct);
        return ToDto(user);
    }

    /// <summary>
    /// Logs in. Wrong password returns 401; 5 failed attempts lock the account for 15 minutes
    /// (Identity LockoutOptions), then 423. Accounts flagged <c>MustChangePassword</c> (e.g. the
    /// seeded admin) get 403 with code <c>MustChangePassword</c> and no tokens until they change
    /// their password via POST /api/auth/change-password.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var outcome = await _users.CheckPasswordAsync(request.Email.Trim(), request.Password, ct);
        switch (outcome)
        {
            case SignInOutcome.LockedOut:
                throw new LockedOutException();
            case SignInOutcome.InvalidCredentials:
            case SignInOutcome.NotFound:
                throw new UnauthorizedException("Invalid email or password.");
        }
        var user = await _users.FindByEmailAsync(request.Email.Trim(), ct)
            ?? throw new UnauthorizedException("Invalid email or password.");
        if (user.MustChangePassword) throw new MustChangePasswordException();
        await _audit.LogAsync(user.Id, null, "Auth.Login", "User", user.Id, null, ct);
        return await IssueTokensAsync(user, ct);
    }

    /// <summary>Rotates a refresh token: the presented token is revoked and a new pair is issued. Unknown/expired/revoked tokens return 401.</summary>
    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var stored = await _refreshTokens.GetByHashAsync(_jwt.HashToken(request.RefreshToken), ct);
        if (stored is null || !stored.IsActive) throw new UnauthorizedException("Invalid or expired refresh token.");
        var user = await _users.FindByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedException("Invalid or expired refresh token.");
        await _refreshTokens.RevokeAsync(stored, ct);
        await _audit.LogAsync(user.Id, null, "Auth.Refresh", "User", user.Id, null, ct);
        return await IssueTokensAsync(user, ct);
    }

    /// <summary>Revokes a refresh token (logout). Unknown tokens are ignored (idempotent).</summary>
    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var stored = await _refreshTokens.GetByHashAsync(_jwt.HashToken(request.RefreshToken), ct);
        if (stored is not null && stored.IsActive) await _refreshTokens.RevokeAsync(stored, ct);
    }

    /// <summary>Changes the current user's password.</summary>
    public async Task ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var ok = await _users.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);
        if (!ok) throw new ValidationException("Current password is incorrect or the new password does not meet the policy.");
        await _audit.LogAsync(userId, null, "Auth.PasswordChanged", "User", userId, null, ct);
    }

    /// <summary>
    /// Changes a password without a login session. Only allowed for accounts flagged
    /// <c>MustChangePassword</c> (e.g. the seeded admin on first login): the caller must still
    /// prove the current password. Any other account gets 403.
    /// </summary>
    public async Task ChangePasswordWithoutLoginAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) throw new ValidationException("Email is required when changing a password without logging in.");
        var user = await _users.FindByEmailAsync(request.Email.Trim(), ct)
            ?? throw new NotFoundException("User not found.");
        if (!user.MustChangePassword) throw new ForbiddenException("Password change without login is only allowed when a password change is required.");
        var outcome = await _users.CheckPasswordAsync(request.Email.Trim(), request.CurrentPassword, ct);
        if (outcome != SignInOutcome.Success) throw new UnauthorizedException("Current password is incorrect.");
        var ok = await _users.ChangePasswordAsync(user.Id, request.CurrentPassword, request.NewPassword, ct);
        if (!ok) throw new ValidationException("The new password does not meet the policy.");
        await _audit.LogAsync(user.Id, null, "Auth.PasswordChanged", "User", user.Id, "First-login password change", ct);
    }

    /// <summary>Returns the current user's profile.</summary>
    public async Task<UserDto> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");
        return ToDto(user);
    }
}
