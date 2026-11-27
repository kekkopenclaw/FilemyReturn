using System.Text;
using DocuArchive.Application;
using DocuArchive.Infrastructure;
using DocuArchive.Infrastructure.Data;
using DocuArchive.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// SECURITY — Production JWT check: the API REFUSES to start in Production when the JWT
// signing key is missing, is the DEV_ONLY placeholder from appsettings.json, or is
// shorter than 32 characters. Set a strong random key via Jwt__Key (env var / secret
// store). The placeholder in appsettings.json is for local development only.
const string devOnlyJwtPlaceholder = "DEV_ONLY_PLACEHOLDER_KEY_CHANGE_ME_via_Jwt__Key_env_var_0123456789abcdef";
var jwtKeyForCheck = builder.Configuration["Jwt:Key"] ?? string.Empty;
if (builder.Environment.IsProduction()
    && (string.IsNullOrWhiteSpace(jwtKeyForCheck)
        || jwtKeyForCheck == devOnlyJwtPlaceholder
        || jwtKeyForCheck.Length < 32))
{
    throw new InvalidOperationException(
        "Refusing to start in Production: Jwt:Key is missing, is the DEV_ONLY placeholder, or is shorter than 32 characters. " +
        "Set a strong random Jwt__Key via environment variable or secret store.");
}

// Configuration placeholders only — real secrets come from env vars / user-secrets
// (Jwt__Key, ConnectionStrings__Default, Database__Provider, Frontend__Origin, Storage__RootPath, ADMIN_PASSWORD).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

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

// Rate limiting: fixed window; stricter on /api/auth/*.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("default", o => { o.PermitLimit = 120; o.Window = TimeSpan.FromMinutes(1); });
    options.AddFixedWindowLimiter("auth", o => { o.PermitLimit = 30; o.Window = TimeSpan.FromMinutes(1); });
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

app.MapHealthChecks("/health");
app.MapControllers(); // Rate-limit policies are set per controller: "auth" on AuthController, "default" elsewhere.

// SPA fallback: unknown non-API routes serve the Angular app (only when wwwroot exists).
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.MapFallbackToFile("index.html");
}

// Development-only seed (admin@docuarchive.local; password from ADMIN_PASSWORD env var,
// or a generated one printed once to the log in Development). Never in Production.
if (app.Environment.IsDevelopment())
{
    await SeedData.EnsureSeededAsync(app.Services);
}

app.Run();

/// <summary>Entry point exposed for WebApplicationFactory integration tests.</summary>
public partial class Program { }
