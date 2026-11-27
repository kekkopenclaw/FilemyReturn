using System.Security.Cryptography;
using DocuArchive.Domain;
using DocuArchive.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocuArchive.Infrastructure.Data;

/// <summary>
/// Development-only seed data. NEVER runs in Production: Program.cs only calls it when
/// the environment is Development, and this method refuses to run in Production too.
///
/// SECURITY: there is NO hardcoded seed password anywhere in this codebase. The seed
/// admin password comes from the <c>ADMIN_PASSWORD</c> environment variable. If it is not
/// set and the environment is not Development, seeding fails loudly. In Development only,
/// if it is unset, a random password is generated at runtime and printed ONCE to the
/// console/log (never written to any file). The seeded admin is flagged
/// <c>MustChangePassword</c> and cannot log in until the password is changed.
/// </summary>
public static class SeedData
{
    public const string AdminEmail = "admin@docuarchive.local";

    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var environment = provider.GetRequiredService<IHostEnvironment>();
        if (environment.IsProduction())
            throw new InvalidOperationException("SeedData must never run in Production.");

        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
        var db = provider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var adminPassword = configuration["ADMIN_PASSWORD"];
        var generatedPassword = false;
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "ADMIN_PASSWORD environment variable is not set. Set ADMIN_PASSWORD to seed the demo admin account.");
            adminPassword = GeneratePassword();
            generatedPassword = true;
        }
        if (adminPassword.Length < 12)
            throw new InvalidOperationException("ADMIN_PASSWORD must be at least 12 characters (password policy).");

        var admin = await users.FindByEmailAsync(AdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = AdminEmail, Email = AdminEmail,
                DisplayName = "Demo Admin", IsSystemAdmin = true,
                MustChangePassword = true
            };
            var result = await users.CreateAsync(admin, adminPassword);
            if (!result.Succeeded)
            {
                logger.LogError("Seed admin creation failed: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }
            logger.LogInformation("Seed admin created: {Email} (must change password on first login)", AdminEmail);
            if (generatedPassword)
            {
                // Development only, printed once, never persisted. Rotate by setting ADMIN_PASSWORD.
                var message = $"[SeedData] Development seed admin password (generated, change it on first login): {adminPassword}";
                logger.LogWarning("{Message}", message);
                Console.WriteLine(message);
            }
        }

        var workspaceRepo = provider.GetRequiredService<IWorkspaceRepository>();
        var existing = await db.Workspaces.FirstOrDefaultAsync(w => w.Name == "Demo Archive");
        if (existing is null)
        {
            var workspace = new Workspace { Name = "Demo Archive", Description = "Seeded demo workspace (Development only).", CreatedById = admin.Id };
            await workspaceRepo.AddAsync(workspace);
            await workspaceRepo.AddMembershipAsync(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id, UserId = admin.Id, Role = WorkspaceRole.WorkspaceOwner
            });
            logger.LogInformation("Seed workspace created: Demo Archive");
        }
    }

    /// <summary>Generates a random 16-character password meeting the policy (upper, lower, digit, symbol).</summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*";
        const string all = upper + lower + digits + symbols;
        var chars = new char[16];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var i = 4; i < chars.Length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        // Fisher-Yates shuffle so the required character classes are not in fixed positions.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
