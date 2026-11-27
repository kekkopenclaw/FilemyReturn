namespace DocuArchive.Application;

/// <summary>JWT settings, bound from the <c>Jwt</c> configuration section (appsettings / env vars <c>Jwt__*</c>). The signing key must come from an environment variable / user-secrets in production — never commit a real key.</summary>
public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "DocuArchive";
    public string Audience { get; set; } = "DocuArchive";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>Application error mapped to an HTTP status by the WebApi exception middleware.</summary>
public class AppException : Exception
{
    public AppException(int statusCode, string message, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
    public int StatusCode { get; }
    /// <summary>Optional machine-readable error code, returned as <c>code</c> in the problem response.</summary>
    public string? Code { get; }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message = "Not found.") : base(404, message) { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message = "Forbidden.") : base(403, message) { }
}

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message = "Unauthorized.") : base(401, message) { }
}

public class LockedOutException : AppException
{
    public LockedOutException(string message = "Account locked. Try again later.") : base(423, message) { }
}

/// <summary>
/// Thrown on login when the account must change its password first (e.g. the seeded
/// admin). Clients must call POST /api/auth/change-password with the current password.
/// </summary>
public class MustChangePasswordException : AppException
{
    public MustChangePasswordException()
        : base(403, "You must change your password before you can log in.", "MustChangePassword") { }
}

public class ValidationException : AppException
{
    public ValidationException(string message) : base(400, message) { }
    public ValidationException(IEnumerable<string> errors) : base(400, string.Join(" ", errors)) => Errors = errors.ToArray();
    public string[] Errors { get; } = [];
}
