using DocuArchive.Application;
using DocuArchive.Domain;

namespace DocuArchive.Tests;

/// <summary>Unit tests for <see cref="MonitoringService"/> with hand-written
/// fakes (no mocking library in this project). The integration tests in
/// MonitoringTests cover the real database and HTTP layer; these prove the
/// service's own logic: the admin check, the aggregation, and the
/// version/uptime helpers.</summary>
public class MonitoringServiceTests
{
    private sealed class FakeDatabase : IDatabaseBackup
    {
        public string Provider { get; set; } = "Sqlite";
        public long SizeBytes { get; set; } = 4096;
        public Task CreateSnapshotAsync(string targetPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default) => Task.FromResult(SizeBytes);
    }

    private sealed class FakeDocuments : IDocumentRepository
    {
        public DocumentStatusCounts Counts { get; set; } = new(5, 2, 1, 1, 1, 1, 1, 0, 3);
        public Task<Document> AddAsync(Document document, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Document?> GetByIdAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Document?> GetByHashAsync(Guid workspaceId, string sha256Hash, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<Document>> ListByWorkspaceAsync(Guid workspaceId, Guid? archiveId, CancellationToken ct = default, string? search = null, bool? needsReview = null, Guid? tagId = null) => throw new NotImplementedException();
        public Task<List<Document>> SearchAsync(Guid workspaceId, DocumentSearchCriteria criteria, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountAllAsync(CancellationToken ct = default) => Task.FromResult(Counts.Total);
        public Task<DocumentStatusCounts> CountByStatusAsync(CancellationToken ct = default) => Task.FromResult(Counts);
        public Task<List<Document>> ListByPhysicalBoxAsync(Guid workspaceId, Guid boxId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Document document, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> TryClaimForOcrAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<Document>> ListQueuedForOcrAsync(int take, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeWorkspaces : IWorkspaceRepository
    {
        public Task<Workspace> AddAsync(Workspace workspace, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Workspace?> GetByIdAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<Workspace>> ListByUserAsync(string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<WorkspaceMembership>> ListMembersAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddMembershipAsync(WorkspaceMembership membership, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveMembershipAsync(Guid workspaceId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateMembershipAsync(WorkspaceMembership membership, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountOwnersAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Workspace workspace, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountMembersAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountAllAsync(CancellationToken ct = default) => Task.FromResult(2);
    }

    private sealed class FakeStorage : IDocumentStorage
    {
        public Task<string> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredFileInfo>> ListFilesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoredFileInfo>>(new[] { new StoredFileInfo("a.pdf", 100), new StoredFileInfo("b.pdf", 250) });
    }

    private sealed class FakeUsers : IUserIdentityService
    {
        public bool Admin { get; set; } = true;
        public bool Exists { get; set; } = true;
        public Task<UserProfile?> FindByEmailAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UserProfile?> FindByIdAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(Exists ? new UserProfile(userId, "admin@example.com", "Admin", Admin, DateTime.UtcNow, false) : null);
        public Task<(bool Succeeded, string[] Errors, UserProfile? User)> CreateUserAsync(string email, string password, string? displayName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignInOutcome> CheckPasswordAsync(string email, string password, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountAllAsync(CancellationToken ct = default) => Task.FromResult(4);
    }

    private sealed class FakeAudit : IAuditRepository
    {
        public int FailedLogins { get; set; } = 3;
        public AuditLog? LatestBackup { get; set; }
        public Task AddAsync(AuditLog entry, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<AuditLog>> ListByWorkspaceAsync(Guid workspaceId, int skip, int take, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountByUserAsync(string userId, string action, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountByActionSinceAsync(string action, DateTime since, CancellationToken ct = default)
            => Task.FromResult(action == "Auth.LoginFailed" ? FailedLogins : 0);
        public Task<AuditLog?> GetLatestByActionAsync(string action, CancellationToken ct = default)
            => Task.FromResult(action == "Backup.Downloaded" ? LatestBackup : null);
    }

    private static MonitoringService Service(
        FakeUsers? users = null, FakeAudit? audit = null, FakeDocuments? docs = null)
        => new(new FakeDatabase(), docs ?? new FakeDocuments(), new FakeWorkspaces(),
            new FakeStorage(), users ?? new FakeUsers(), audit ?? new FakeAudit());

    [Fact]
    public async Task GetStatusAsync_AsAdmin_ReturnsAggregatedCounts()
    {
        var audit = new FakeAudit
        {
            LatestBackup = new AuditLog
            {
                UserId = "admin", Action = "Backup.Downloaded",
                Details = "1 documents, 1 files, 1234 bytes in docuarchive-backup-20261127-080000.zip",
                CreatedAt = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc),
            },
        };
        var status = await Service(audit: audit).GetStatusAsync("admin");

        Assert.Equal("Sqlite", status.DatabaseProvider);
        Assert.Equal(4096, status.DatabaseSizeBytes);
        Assert.Equal(4, status.UserCount);
        Assert.Equal(2, status.WorkspaceCount);
        Assert.Equal(5, status.DocumentCount);
        Assert.Equal(2, status.DocumentsByStatus.Uploaded);
        Assert.Equal(1, status.DocumentsByStatus.Processing);
        Assert.Equal(1, status.DocumentsByStatus.Ready);
        Assert.Equal(1, status.DocumentsByStatus.Failed);
        Assert.Equal(1, status.DocumentsByStatus.OcrQueued);
        Assert.Equal(1, status.DocumentsByStatus.OcrProcessing);
        Assert.Equal(0, status.DocumentsByStatus.OcrFailed);
        Assert.Equal(3, status.DocumentsByStatus.NeedsReview);
        Assert.Equal(2, status.FileCount);
        Assert.Equal(350, status.StorageSizeBytes);
        Assert.Equal(3, status.FailedLoginsLast24h);
        Assert.Equal(new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc), status.LastBackupAtUtc);
        Assert.Contains("docuarchive-backup-20261127-080000.zip", status.LastBackupDetails);
        Assert.True(status.UptimeSeconds > 0);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
    }

    [Fact]
    public async Task GetStatusAsync_NoBackupYet_ReportsNullLastBackup()
    {
        var status = await Service().GetStatusAsync("admin");
        Assert.Null(status.LastBackupAtUtc);
        Assert.Null(status.LastBackupDetails);
    }

    [Fact]
    public async Task GetStatusAsync_NonAdmin_ThrowsForbidden()
    {
        var users = new FakeUsers { Admin = false };
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(users: users).GetStatusAsync("user-1"));
    }

    [Fact]
    public async Task GetStatusAsync_UnknownUser_ThrowsForbidden()
    {
        var users = new FakeUsers { Exists = false };
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(users: users).GetStatusAsync("nobody"));
    }

    [Fact]
    public void UptimeSeconds_IsPositiveForPastStart()
    {
        Assert.True(MonitoringService.UptimeSeconds(DateTime.UtcNow.AddMinutes(-5)) > 290);
        Assert.True(MonitoringService.UptimeSeconds(DateTime.UtcNow.AddMinutes(-5)) < 310);
    }

    [Fact]
    public void AppVersion_IsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(MonitoringService.AppVersion()));
    }

    [Fact]
    public void DeployedCommit_ReadsRenderEnvVar()
    {
        var previous = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT");
        try
        {
            Environment.SetEnvironmentVariable("RENDER_GIT_COMMIT", "abc123def");
            Assert.Equal("abc123def", MonitoringService.DeployedCommit());
            Environment.SetEnvironmentVariable("RENDER_GIT_COMMIT", null);
            Assert.Null(MonitoringService.DeployedCommit());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RENDER_GIT_COMMIT", previous);
        }
    }
}
