using DocuArchive.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DocuArchive.Application;

/// <summary>Registers Application-layer services. Application references Domain only (project-wise); the JWT library is a NuGet package, not a project dependency.</summary>
public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<AuthService>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<ArchiveService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentUploadService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AuditQueryService>();
        return services;
    }
}
