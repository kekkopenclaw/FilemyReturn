using System.Diagnostics;
using System.Reflection;
using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>
/// Phase 9 monitoring: a read-only health snapshot of the whole system for
/// system administrators — process uptime, app version/commit, database
/// provider, counts (users, workspaces, documents by status, OCR queue,
/// human-review queue), storage, failed logins in the last 24h, and when the
/// last backup was downloaded.
///
/// In-app only, by design: there are no external uptime pings and no alerts
/// (that would need a third-party account — see docs/MONITORING.md). The
/// numbers come from the database and the running process; nothing here
/// writes, deletes or changes state, and the response carries no secrets and
/// no per-user or per-workspace detail.
///
/// Version/commit, honestly stated: <c>Version</c> is the assembly
/// informational version; <c>Commit</c> is Render's <c>RENDER_GIT_COMMIT</c>
/// env var and is null on a local run, where no deployed commit exists —
/// never invented.
/// </summary>
public class MonitoringService
{
    private readonly IDatabaseBackup _database;
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IDocumentStorage _storage;
    private readonly IUserIdentityService _users;
    private readonly IAuditRepository _audit;

    public MonitoringService(IDatabaseBackup database, IDocumentRepository documents,
        IWorkspaceRepository workspaces, IDocumentStorage storage,
        IUserIdentityService users, IAuditRepository audit)
    {
        _database = database; _documents = documents; _workspaces = workspaces;
        _storage = storage; _users = users; _audit = audit;
    }

    /// <summary>System administrators only — the snapshot covers every workspace's data in aggregate.</summary>
    private async Task RequireSystemAdminAsync(string userId, CancellationToken ct)
    {
        var profile = await _users.FindByIdAsync(userId, ct);
        if (profile is null || !profile.IsSystemAdmin)
            throw new ForbiddenException("Only a system administrator can view monitoring.");
    }

    /// <summary>Process uptime in seconds (time since this process started).</summary>
    public static double UptimeSeconds(DateTime startedAtUtc)
        => Math.Max(0, (DateTime.UtcNow - startedAtUtc).TotalSeconds);

    /// <summary>
    /// App version from the entry assembly's informational version, or
    /// "unknown" when the assembly carries none (e.g. a test host).
    /// </summary>
    public static string AppVersion()
        => Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Deployed commit from Render's <c>RENDER_GIT_COMMIT</c> env var, or
    /// null when the app was not deployed from git that way (local runs).
    /// </summary>
    public static string? DeployedCommit()
    {
        var commit = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT");
        return string.IsNullOrWhiteSpace(commit) ? null : commit.Trim();
    }

    public async Task<MonitoringStatusDto> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        await RequireSystemAdminAsync(userId, ct);
        var counts = await _documents.CountByStatusAsync(ct);
        var files = await _storage.ListFilesAsync(ct);
        var lastBackup = await _audit.GetLatestByActionAsync("Backup.Downloaded", ct);
        return new MonitoringStatusDto(
            Version: AppVersion(),
            Commit: DeployedCommit(),
            UptimeSeconds: UptimeSeconds(Process.GetCurrentProcess().StartTime.ToUniversalTime()),
            DatabaseProvider: _database.Provider,
            DatabaseSizeBytes: await _database.GetDatabaseSizeBytesAsync(ct),
            UserCount: await _users.CountAllAsync(ct),
            WorkspaceCount: await _workspaces.CountAllAsync(ct),
            DocumentCount: counts.Total,
            DocumentsByStatus: new DocumentCountsDto(
                counts.Total, counts.Uploaded, counts.Processing, counts.Ready, counts.Failed,
                counts.OcrQueued, counts.OcrProcessing, counts.OcrFailed, counts.NeedsReview),
            FileCount: files.Count,
            StorageSizeBytes: files.Sum(f => f.SizeBytes),
            FailedLoginsLast24h: await _audit.CountByActionSinceAsync(
                "Auth.LoginFailed", DateTime.UtcNow.AddHours(-24), ct),
            LastBackupAtUtc: lastBackup?.CreatedAt,
            LastBackupDetails: lastBackup?.Details,
            GeneratedAtUtc: DateTime.UtcNow);
    }
}
