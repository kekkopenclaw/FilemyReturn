using DocuArchive.Application;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using DocuArchive.Infrastructure.Email;
using DocuArchive.Infrastructure.Identity;
using DocuArchive.Infrastructure.Ocr;
using DocuArchive.Infrastructure.Services;
using DocuArchive.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Infrastructure;

/// <summary>
/// Registers Infrastructure services: EF Core DbContext (provider-switchable),
/// ASP.NET Core Identity, repositories, and the Domain service implementations.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database: Database__Provider = Sqlite (default, local dev) | Postgres (prod).
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connection = configuration.GetConnectionString("Default")
            ?? "Data Source=docuarchive.db";
        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
                options.UseNpgsql(connection);
            else
                options.UseSqlite(connection);
        });

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            // Strong password policy (Phase 1 spec).
            options.Password.RequiredLength = 12;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredUniqueChars = 1;
            options.User.RequireUniqueEmail = true;
            // Account lockout: 5 failed logins -> 15 minute lockout.
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<IUserIdentityService, IdentityUserService>();
        services.AddScoped<IWorkspaceRepository, EfWorkspaceRepository>();
        services.AddScoped<IArchiveRepository, EfArchiveRepository>();
        services.AddScoped<ITagRepository, EfTagRepository>();
        services.AddScoped<IFilingRuleRepository, EfFilingRuleRepository>();
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IAuditRepository, EfAuditRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
        services.AddScoped<IDocumentStorage, LocalFileSystemStorage>();
        // Phase 3 OCR: local Tesseract CLI (free, CPU-only, nothing leaves the machine).
        // PaddleOCR stays an unwired stub — see docs/OCR.md for the evaluation.
        services.Configure<OcrSettings>(configuration.GetSection("Ocr"));
        services.AddScoped<IOcrEngine, TesseractOcrEngine>();
        services.AddScoped<IEmailService, LogEmailService>();

        services.AddApplication();
        return services;
    }
}
