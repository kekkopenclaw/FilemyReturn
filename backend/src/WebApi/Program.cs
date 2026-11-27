using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using DocuArchive.Application;
using DocuArchive.Infrastructure;
using DocuArchive.Infrastructure.Data;
using DocuArchive.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// DEPLOY (Render etc.): listen on the $PORT env var when set (Render injects it).
// Validated to 1-65535; when unset, ASP.NET Core defaults apply (launchSettings / 5000).
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv))
{
    if (!int.TryParse(portEnv, out var port) || port is < 1 or > 65535)
        throw new InvalidOperationException($"Invalid PORT environment variable: '{portEnv}'. Expected a number 1-65535.");
    builder.WebHost.UseUrls($"http://*:{port}");
}

// SECURITY — JWT check: the API REFUSES to start when the JWT signing key is missing,
// is the DEV_ONLY placeholder from appsettings.json, or is shorter than 32 characters.
// The placeholder is only acceptable in Development (local dev default). In every other
// environment (Production, Staging, Testing, ...) set a strong random key via Jwt__Key
// (env var / secret store). The placeholder in appsettings.json is for local development only.
const string devOnlyJwtPlaceholder = "DEV_ONLY_PLACEHOLDER_KEY_CHANGE_ME_via_Jwt__Key_env_var_0123456789abcdef";
var jwtKeyForCheck = builder.Configuration["Jwt:Key"] ?? string.Empty;
if (!builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(jwtKeyForCheck)
        || jwtKeyForCheck == devOnlyJwtPlaceholder
        || jwtKeyForCheck.Length < 32))
{
    throw new InvalidOperationException(
        "Refusing to start: Jwt:Key is missing, is the DEV_ONLY placeholder, or is shorter than 32 characters. " +
        "Set a strong random Jwt__Key via environment variable or secret store.");
}

// Configuration placeholders only — real secrets come from env vars / user-secrets
// (Jwt__Key, ConnectionStrings__Default, Database__Provider, Frontend__Origin, Storage__RootPath, ADMIN_PASSWORD).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
// Health: /health is liveness only (process up, no DB dependency) so a database
// outage does not make the container look dead; /health/ready includes the DB check.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// CORS: locked to the configured frontend origin (default http://localhost:4200).
var frontendOrigin = builder.Configuration["Frontend:Origin"] ?? "http://localhost:4200";
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(frontendOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// JWT bearer auth.
var jwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
// NOTE: AddIdentity (in AddInfrastructure) sets the cookie schemes as the explicit
// authentication defaults. They must be overridden here — otherwise [Authorize]
// challenges with a redirect to /Account/Login and Bearer tokens are never validated.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "DocuArchive",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "DocuArchive",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();

// Rate limiting, partitioned per client — never one global bucket shared by all callers.
// "default": one fixed window per client IP (120/min). "auth": one fixed window per
// client IP + account email (30/min), so a brute-force attack on one account cannot
// exhaust the budget for everyone else, and one IP cannot hammer many accounts either.
// The email is stashed by the middleware below (best-effort; falls back to IP-only).
// Account-level brute force is additionally stopped by Identity lockout (5 failures -> 423).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("default", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ClientIp(context),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: AuthPartitionKey(context),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0, AutoReplenishment = true
        }));
});

// Swagger / OpenAPI (Swashbuckle, MIT licence, free and open-source).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DocuArchive API", Version = "v1", Description = "Secure AI Document Digitisation & Archive Management Platform — Phase 1 API." });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer",
        BearerFormat = "JWT", In = ParameterLocation.Header,
        Description = "JWT access token. Prefix with: Bearer {token}"
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", null!), new List<string>() }
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "DocuArchive API v1"));
}

app.UseCors("Frontend");

// Best-effort: stash the login email (first 4 KiB of a JSON auth request body) so the
// "auth" rate limiter can partition per account as well as per IP. Never fails the
// request: any read/parse problem falls back to IP-only partitioning. The body is
// buffered and rewound so model binding still sees it untouched.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.StartsWithSegments("/api/auth")
        && context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
    {
        try
        {
            context.Request.EnableBuffering();
            var buffer = new byte[4096];
            var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
            context.Request.Body.Position = 0;
            if (read > 0)
            {
                using var json = JsonDocument.Parse(buffer.AsMemory(0, read));
                if (json.RootElement.TryGetProperty("email", out var email)
                    && email.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(email.GetString()))
                {
                    context.Items["AuthEmail"] = email.GetString()!;
                }
            }
        }
        catch
        {
            // Best-effort only: a body that cannot be read/parsed just means IP-only limiting.
            try { context.Request.Body.Position = 0; } catch { /* body unusable; let model binding report it */ }
        }
    }
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// DEPLOY (single deploy): serve the Angular production build from wwwroot when it exists.
// The Angular build output is copied into src/WebApi/wwwroot at deploy time (see
// backend/README.md). When wwwroot is absent (local API-only dev), this is a no-op and
// the API works exactly as before.
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Liveness: no checks, so the process reports healthy even when the DB is down.
// Readiness: runs the tagged checks (database) — use for deploy gates, not liveness.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapControllers(); // Rate-limit policies are set per controller: "auth" on AuthController, "default" elsewhere.

// SPA fallback: unknown non-API routes serve the Angular app (only when wwwroot exists).
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.MapFallbackToFile("index.html");
}

// Startup: migrate the database in ALL environments before serving traffic.
// Production never ran migrations before this change (MigrateAsync only ran inside the
// Development-only seed), so a fresh Production database had no tables at all.
await SeedData.MigrateAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    // Development-only demo seed (admin@docuarchive.local; password from ADMIN_PASSWORD
    // env var, or a generated one printed once to the log). Never in Production.
    await SeedData.EnsureSeededAsync(app.Services);
}
else if (app.Environment.IsProduction())
{
    // Production first-run bootstrap: creates admin@docuarchive.local from ADMIN_PASSWORD
    // only when no users exist yet. Throws at startup when ADMIN_PASSWORD is missing.
    await SeedData.EnsureProductionAdminAsync(app.Services);
}

app.Run();

/// <summary>Entry point exposed for WebApplicationFactory integration tests.</summary>
public partial class Program
{
    /// <summary>Client IP for rate-limit partitioning ("unknown" when unavailable).</summary>
    internal static string ClientIp(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Auth rate-limit partition: client IP + account email when the login email was
    /// stashed by the middleware above; IP-only otherwise.
    /// </summary>
    internal static string AuthPartitionKey(HttpContext context)
    {
        var ip = ClientIp(context);
        return context.Items["AuthEmail"] is string email
            ? $"{ip}:{email.Trim().ToLowerInvariant()}"
            : ip;
    }
}
