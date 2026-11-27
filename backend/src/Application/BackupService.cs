using System.IO.Compression;
using System.Text.Json;
using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>A finished backup archive on disk, ready to stream to the admin who asked for it.</summary>
public record BackupArchive(string TempFilePath, string FileName, BackupManifestDto Manifest);

/// <summary>
/// Phase 8 backup: a system administrator exports the whole system — a
/// consistent database snapshot plus every stored file — as one timestamped
/// ZIP, and downloads it off the server.
///
/// Why this shape: on the Render Free tier the SQLite file and the uploaded
/// files live on ephemeral disk and are wiped on every redeploy, so the only
/// backup that survives is one the admin downloads and keeps elsewhere. A
/// scheduled in-app backup would write to the same ephemeral disk and vanish
/// with the data it was meant to protect, so there is no scheduler — the
/// admin downloads on demand (before every deploy, at least), and
/// docs/BACKUP.md says so plainly.
///
/// Scope, honestly stated: the database snapshot is SQLite-only
/// (<c>VACUUM INTO</c>). A PostgreSQL deployment backs up with pg_dump or
/// its provider's backup — the download endpoint says so with a 501 instead
/// of shipping a broken export. Restore is a documented manual procedure
/// (docs/BACKUP.md): stop the app, replace the database file and the storage
/// directory, restart. There is deliberately no in-app restore endpoint —
/// letting an upload overwrite the live database is a far bigger security
/// surface than the backup itself.
/// </summary>
public class BackupService
{
    private readonly IDatabaseBackup _database;
    private readonly IDocumentRepository _documents;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IDocumentStorage _storage;
    private readonly IUserIdentityService _users;
    private readonly IAuditService _audit;

    public BackupService(IDatabaseBackup database, IDocumentRepository documents,
        IWorkspaceRepository workspaces, IDocumentStorage storage,
        IUserIdentityService users, IAuditService audit)
    {
        _database = database; _documents = documents; _workspaces = workspaces;
        _storage = storage; _users = users; _audit = audit;
    }

    /// <summary>System administrators only — a backup contains every workspace's data.</summary>
    private async Task RequireSystemAdminAsync(string userId, CancellationToken ct)
    {
        var profile = await _users.FindByIdAsync(userId, ct);
        if (profile is null || !profile.IsSystemAdmin)
            throw new ForbiddenException("Only a system administrator can download a backup.");
    }

    /// <summary>What a backup would contain right now (counts and sizes, no files read).</summary>
    public async Task<BackupInfoDto> GetInfoAsync(string userId, CancellationToken ct = default)
    {
        await RequireSystemAdminAsync(userId, ct);
        var files = await _storage.ListFilesAsync(ct);
        return new BackupInfoDto(
            _database.Provider,
            await _database.GetDatabaseSizeBytesAsync(ct),
            await _documents.CountAllAsync(ct),
            await _workspaces.CountAllAsync(ct),
            files.Count,
            files.Sum(f => f.SizeBytes),
            DateTime.UtcNow);
    }

    /// <summary>
    /// Builds the backup ZIP in a temp file and returns its path. The caller
    /// (the controller) streams it and deletes the temp file once the
    /// response has been sent — the archive must never linger on the server.
    /// The ZIP holds <c>database.sqlite</c> (a <c>VACUUM INTO</c> snapshot),
    /// <c>files/&lt;storage key&gt;</c> for every stored file, and a
    /// <c>manifest.json</c> describing the contents.
    /// </summary>
    public async Task<BackupArchive> CreateBackupAsync(string userId, CancellationToken ct = default)
    {
        await RequireSystemAdminAsync(userId, ct);
        if (!_database.Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            throw new AppException(501,
                "Database backups for PostgreSQL are taken with pg_dump or your database provider's backup — see docs/BACKUP.md.");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var fileName = $"docuarchive-backup-{stamp}.zip";
        var tempDir = Path.Combine(Path.GetTempPath(), "docuarchive-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var snapshotPath = Path.Combine(tempDir, "database.sqlite");
        var zipPath = Path.Combine(tempDir, fileName);
        var succeeded = false;
        try
        {
            // Consistent snapshot first — if this fails, nothing else runs
            // and no partial archive is ever served.
            await _database.CreateSnapshotAsync(snapshotPath, ct);
            var files = await _storage.ListFilesAsync(ct);
            var manifest = new BackupManifestDto(
                BackupVersion: 1,
                Provider: _database.Provider,
                GeneratedAtUtc: DateTime.UtcNow,
                DatabaseSizeBytes: new FileInfo(snapshotPath).Length,
                DocumentCount: await _documents.CountAllAsync(ct),
                WorkspaceCount: await _workspaces.CountAllAsync(ct),
                FileCount: files.Count,
                StorageSizeBytes: files.Sum(f => f.SizeBytes));

            await using (var zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                zip.CreateEntryFromFile(snapshotPath, "database.sqlite", CompressionLevel.Optimal);
                foreach (var file in files)
                {
                    var stream = await _storage.OpenReadAsync(file.Key, ct);
                    if (stream is null) continue; // File vanished between listing and archiving — skip it, don't fail the backup.
                    await using (stream)
                    {
                        var entry = zip.CreateEntry($"files/{file.Key}", CompressionLevel.Optimal);
                        await using var entryStream = entry.Open();
                        await stream.CopyToAsync(entryStream, ct);
                    }
                }
                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest,
                    new JsonSerializerOptions { WriteIndented = true }, ct);
            }

            await _audit.LogAsync(userId, null, "Backup.Downloaded", "Backup", null,
                $"{manifest.DocumentCount} documents, {manifest.FileCount} files, " +
                $"{manifest.DatabaseSizeBytes + manifest.StorageSizeBytes} bytes in {fileName}", ct);
            succeeded = true;
            return new BackupArchive(zipPath, fileName, manifest);
        }
        finally
        {
            // The snapshot is inside the ZIP now — remove the intermediate
            // copy whatever happens. On failure the whole temp directory
            // (including a partial ZIP) is removed, so a failed backup never
            // leaves an archive behind. On success the ZIP itself is the
            // caller's to stream and delete.
            try { if (File.Exists(snapshotPath)) File.Delete(snapshotPath); } catch { /* best-effort cleanup */ }
            if (!succeeded)
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
