using System.Security.Claims;
using DocuArchive.Application;
using DocuArchive.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuArchive.WebApi.Controllers;

/// <summary>
/// Authentication endpoints. Thin controller — all logic lives in <see cref="AuthService"/> (Application).
///
/// SECURITY: the refresh token is delivered ONLY as an HttpOnly cookie
/// (<c>docuarchive.refresh</c>), never in a response body and never in
/// localStorage — a script injected into the page (XSS) cannot read it.
/// The cookie is scoped to /api/auth (the only routes that need it),
/// SameSite=Lax (same-site in production, where the API serves the Angular
/// app, and same-site in dev, where the app runs on localhost:4200 against
/// the API on localhost:5000), and Secure in every environment except
/// Development (local dev runs over plain http). The access token still
/// travels in JSON and lives in frontend memory only (15 minutes).
/// Non-browser API clients that already hold a token may still send it in
/// the request body; the cookie wins when both are present.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "docuarchive.refresh";

    private readonly AuthService _auth;
    private readonly IJwtService _jwt;
    private readonly IWebHostEnvironment _env;

    public AuthController(AuthService auth, IJwtService jwt, IWebHostEnvironment env)
    {
        _auth = auth;
        _jwt = jwt;
        _env = env;
    }

    private static string UserId(ControllerBase c) =>
        c.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? c.User.FindFirstValue("sub")
        ?? throw new UnauthorizedException("Missing user id claim.");

    /// <summary>Sets the refresh-token cookie from an issued token pair.</summary>
    private void SetRefreshCookie(string refreshToken) => Response.Cookies.Append(
        RefreshCookieName,
        refreshToken,
        new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = !_env.IsDevelopment(),
            Path = "/api/auth",
            MaxAge = _jwt.RefreshTokenLifetime,
        });

    /// <summary>Expires the refresh-token cookie (logout).</summary>
    private void ClearRefreshCookie() => Response.Cookies.Append(
        RefreshCookieName,
        string.Empty,
        new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = !_env.IsDevelopment(),
            Path = "/api/auth",
            Expires = DateTimeOffset.UnixEpoch,
        });

    /// <summary>Refresh token from the HttpOnly cookie, falling back to the request body.</summary>
    private string? ResolveRefreshToken(string? bodyToken)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var cookie) && !string.IsNullOrWhiteSpace(cookie))
            return cookie;
        return string.IsNullOrWhiteSpace(bodyToken) ? null : bodyToken;
    }

    /// <summary>Strips the refresh token out of a response body — it travels by cookie only.</summary>
    private static AuthResponse WithoutRefreshToken(AuthResponse res) => res with { RefreshToken = string.Empty };

    /// <summary>Registers a new user account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<UserDto>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var user = await _auth.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Me), new { }, user);
    }

    /// <summary>Logs in and returns an access token (15 min). The refresh token (7 days) is set as an HttpOnly cookie, not in the body.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var res = await _auth.LoginAsync(request, ct);
        SetRefreshCookie(res.RefreshToken);
        return Ok(WithoutRefreshToken(res));
    }

    /// <summary>Rotates a refresh token and returns a new token pair (new cookie, empty body token).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var token = ResolveRefreshToken(request.RefreshToken)
            ?? throw new UnauthorizedException("Invalid or expired refresh token.");
        var res = await _auth.RefreshAsync(new RefreshRequest(token), ct);
        SetRefreshCookie(res.RefreshToken);
        return Ok(WithoutRefreshToken(res));
    }

    /// <summary>Revokes a refresh token (logout) and clears the cookie. Idempotent — no token, no problem.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        var token = ResolveRefreshToken(request.RefreshToken);
        if (token is not null)
            await _auth.LogoutAsync(new LogoutRequest(token), ct);
        ClearRefreshCookie();
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
