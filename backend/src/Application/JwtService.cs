using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DocuArchive.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocuArchive.Application;

/// <summary>
/// Creates signed JWT access tokens (15 min) and opaque refresh tokens (7 days).
/// Refresh tokens are stored only as SHA-256 hashes (see <see cref="IRefreshTokenRepository"/>)
/// and rotated on every use. Pure token logic — no Identity dependency.
/// </summary>
public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    public JwtService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
        if (string.IsNullOrWhiteSpace(_settings.Key) || _settings.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key is missing or too short (minimum 32 characters). Set it via the Jwt__Key environment variable or user-secrets.");
    }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_settings.AccessTokenMinutes);
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_settings.RefreshTokenDays);

    /// <inheritdoc/>
    public string CreateAccessToken(UserProfile user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("display_name", user.DisplayName ?? string.Empty),
            new("system_admin", user.IsSystemAdmin ? "true" : "false")
        };
        if (user.IsSystemAdmin) claims.Add(new Claim(ClaimTypes.Role, "SystemAdmin"));
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(AccessTokenLifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    /// <summary>
    /// Generates a new opaque refresh token (plaintext — only its hash is stored).
    /// Base64url (RFC 4648 §5: no <c>+</c>, <c>/</c> or <c>=</c> padding) so the
    /// token can travel in an HttpOnly cookie verbatim — plain base64 would be
    /// percent-encoded by <c>Response.Cookies.Append</c> (<c>/</c> → <c>%2F</c>)
    /// and the encoded form would no longer match the stored hash.
    /// </summary>
    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <inheritdoc/>
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
