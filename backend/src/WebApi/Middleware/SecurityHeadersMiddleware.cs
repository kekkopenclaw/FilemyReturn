namespace DocuArchive.WebApi.Middleware;

/// <summary>
/// Security headers on every response (set before the rest of the pipeline runs, so error
/// responses carry them too). The Angular SPA loads everything from its own origin — no
/// external scripts, fonts or images — so a strict same-origin CSP is safe here.
/// </summary>
public class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        // Swagger UI (Development only) ships inline scripts that a strict CSP would block;
        // it is never served in Production, so the policy is skipped for its paths only.
        if (!context.Request.Path.StartsWithSegments("/swagger"))
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
        return _next(context);
    }
}
