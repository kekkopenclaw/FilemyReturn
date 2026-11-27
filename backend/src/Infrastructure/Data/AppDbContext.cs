using DocuArchive.Domain;
using DocuArchive.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocuArchive.Infrastructure.Data;

/// <summary>
/// EF Core DbContext. Provider is config-switchable (SQLite local dev, PostgreSQL prod)
/// via <c>Database__Provider</c> / <c>ConnectionStrings__Default</c>; see
/// <see cref="InfrastructureServiceExtensions"/>.
/// No global query filters are used — workspace isolation is enforced explicitly in
/// services (WorkspaceAccessService) and tested.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<Archive> Archives => Set<Archive>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Workspace>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Name).IsRequired().HasMaxLength(200);
            e.Property(w => w.Description).HasMaxLength(1000);
            e.HasIndex(w => w.CreatedAt);
            // CreatedById is a plain value (the Identity user lives in Infrastructure, not Domain).
        });

        modelBuilder.Entity<WorkspaceMembership>(e =>
        {
            e.HasKey(m => new { m.WorkspaceId, m.UserId });
            e.HasIndex(m => m.UserId);
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(32);
            e.HasOne(m => m.Workspace).WithMany(w => w.Memberships).HasForeignKey(m => m.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Archive>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(a => a.WorkspaceId);
            e.HasIndex(a => a.CreatedAt);
            e.HasOne(a => a.Workspace).WithMany(w => w.Archives).HasForeignKey(a => a.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(500);
            e.Property(d => d.StorageKey).IsRequired().HasMaxLength(500);
            e.Property(d => d.Sha256Hash).IsRequired().HasMaxLength(64);
            e.Property(d => d.MimeType).IsRequired().HasMaxLength(200);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(d => d.OcrStatus).HasConversion<string>().HasMaxLength(32);
            // Phase 3 receipt extraction (all nullable until OCR runs).
            e.Property(d => d.OcrText);
            e.Property(d => d.OcrConfidence);
            e.Property(d => d.ExtractedVendor).HasMaxLength(200);
            e.Property(d => d.ExtractedDate);
            e.Property(d => d.ExtractedTotal).HasColumnType("decimal(18,2)");
            e.Property(d => d.ExtractedCurrency).HasMaxLength(8);
            e.Property(d => d.SuggestedCategory).HasMaxLength(64);
            // Phase 3 classification (all null/false until OCR + classification run).
            e.Property(d => d.ClassificationConfidence);
            e.Property(d => d.ClassificationReason).HasMaxLength(500);
            e.Property(d => d.NeedsReview);
            e.Property(d => d.ConfirmedCategory).HasMaxLength(64);
            e.HasIndex(d => d.NeedsReview); // the dashboard review queue counts flagged documents.
            e.HasIndex(d => d.OcrStatus); // the OCR worker polls for Queued documents.
            e.HasIndex(d => d.WorkspaceId);
            e.HasIndex(d => d.ArchiveId);
            e.HasIndex(d => d.CreatedAt);
            e.HasIndex(d => new { d.WorkspaceId, d.Sha256Hash }).IsUnique();
            e.HasOne(d => d.Workspace).WithMany(w => w.Documents).HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Archive).WithMany(a => a.Documents).HasForeignKey(d => d.ArchiveId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Action).IsRequired().HasMaxLength(200);
            e.HasIndex(a => a.WorkspaceId);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.CreatedAt);
            e.HasOne(a => a.Workspace).WithMany().HasForeignKey(a => a.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.TokenHash).IsRequired().HasMaxLength(128);
            e.HasIndex(r => r.TokenHash).IsUnique();
            e.HasIndex(r => r.UserId);
            e.HasIndex(r => r.ExpiresAt);
        });
    }
}
