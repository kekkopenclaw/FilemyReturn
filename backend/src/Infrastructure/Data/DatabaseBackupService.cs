using DocuArchive.Domain;
using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DocuArchive.Infrastructure.Data;

/// <summary>
/// Database backup primitives (Phase 8). SQLite only, in-app: <c>VACUUM INTO</c>
/// writes a consistent snapshot of the live database without stopping the
/// app (SQLite takes the write lock itself, so concurrent readers and the
/// OCR worker are not disturbed beyond a brief pause). PostgreSQL has no
/// equivalent single-file snapshot — its backups come from pg_dump or the
/// database provider, so <see cref="CreateSnapshotAsync"/> refuses there
/// instead of shipping a half-working export (see docs/BACKUP.md).
/// </summary>
public class DatabaseBackupService : IDatabaseBackup
{
    private readonly AppDbContext _db;
    private readonly string _provider;

    public DatabaseBackupService(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _provider = configuration["Database:Provider"] ?? "Sqlite";
    }

    /// <inheritdoc/>
    public string Provider => _provider;

    /// <inheritdoc/>
    public async Task CreateSnapshotAsync(string targetPath, CancellationToken ct = default)
    {
        if (!IsSqlite())
            throw new NotSupportedException(
                "Database backups for PostgreSQL are taken with pg_dump or your database provider's backup — see docs/BACKUP.md.");
        // VACUUM INTO takes its path as a SQL string literal: double any
        // single quotes (the path is a generated temp path, so this is belt
        // and braces, never user input). SQLite does not accept a parameter
        // for the VACUUM INTO filename, so the literal is built by hand —
        // the EF1002/EF1003 warnings are suppressed for this one call only,
        // with the sanitisation above as the justification.
#pragma warning disable EF1002, EF1003
        var escaped = targetPath.Replace("'", "''", StringComparison.Ordinal);
        await _db.Database.ExecuteSqlRawAsync("VACUUM INTO '" + escaped + "'", ct);
#pragma warning restore EF1002, EF1003
    }

    /// <inheritdoc/>
    public async Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default)
    {
        if (IsSqlite())
        {
            // Works for file-backed and in-memory SQLite alike. The column
            // must be aliased "Value": SqlQueryRaw<long> reads it by name.
            var size = await _db.Database
                .SqlQueryRaw<long>("SELECT page_count * page_size AS Value FROM pragma_page_count(), pragma_page_size()")
                .SingleAsync(ct);
            return size;
        }
        // PostgreSQL: size of the current database, in bytes.
        return await _db.Database
            .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS Value")
            .SingleAsync(ct);
    }

    private bool IsSqlite() => _provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
}
