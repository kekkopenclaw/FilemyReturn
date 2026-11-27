using System.Security.Claims;
using DocuArchive.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuArchive.WebApi.Controllers;

/// <summary>Authentication endpoints. Thin controller — all logic lives in <see cref="AuthService"/> (Application).</summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    private static string UserId(ControllerBase c) =>
        c.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? c.User.FindFirstValue("sub")
        ?? throw new UnauthorizedException("Missing user id claim.");

    /// <summary>Registers a new user account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<UserDto>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var user = await _auth.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Me), new { }, user);
    }

    /// <summary>Logs in and returns an access token (15 min) + refresh token (7 days).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>Rotates a refresh token and returns a new token pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(request, ct));

    /// <summary>Revokes a refresh token (logout).</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request, ct);
        return NoContent();
    }

    /// <summary>
    /// Changes a password. Authenticated users change their own. Unauthenticated callers
    /// must supply <c>Email</c> and are only allowed when that account is flagged
    /// <c>MustChangePassword</c> (first-login flow) — the current password is still required.
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await _auth.ChangePasswordAsync(UserId(this), request, ct);
        }
        else
        {
            await _auth.ChangePasswordWithoutLoginAsync(request, ct);
        }
        return NoContent();
    }

    /// <summary>Returns the current user's profile.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _auth.GetProfileAsync(UserId(this), ct));
}
