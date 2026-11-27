using DocuArchive.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuArchive.WebApi.Controllers;

/// <summary>
/// System-administration endpoints (Phase 8 backup). Thin controller — logic
/// lives in <see cref="BackupService"/> (Application).
///
/// SECURITY: a backup contains every workspace's data, so both endpoints are
/// system-admin only. The controller enforces it with the <c>SystemAdmin</c>
/// role claim (issued at login for system admins); the service re-checks the
/// flag against the database, so a user demoted after login loses access
/// immediately instead of keeping it until the 15-minute access token expires.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SystemAdmin")]
[EnableRateLimiting("default")]
public class AdminController : ControllerBase
{
    private readonly BackupService _backup;
    public AdminController(BackupService backup) => _backup = backup;

    /// <summary>What a backup would contain right now (counts and sizes).</summary>
    [HttpGet("backup/info")]
    public async Task<ActionResult<BackupInfoDto>> BackupInfo(CancellationToken ct)
        => Ok(await _backup.GetInfoAsync(ControllerUser.Id(this), ct));

    /// <summary>
    /// Downloads the backup ZIP (database snapshot + stored files + manifest).
    /// The archive is built in a temp directory that is deleted once the
    /// response has been sent — it must never linger on the server.
    /// </summary>
    [HttpGet("backup/download")]
    public async Task<IActionResult> DownloadBackup(CancellationToken ct)
    {
        var archive = await _backup.CreateBackupAsync(ControllerUser.Id(this), ct);
        var tempDir = Path.GetDirectoryName(archive.TempFilePath);
        Response.OnCompleted(() =>
        {
            // Best-effort cleanup after the response is fully sent.
            try { if (tempDir is not null && Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { /* already gone or locked — temp dir, not user data */ }
            return Task.CompletedTask;
        });
        return PhysicalFile(archive.TempFilePath, "application/zip", archive.FileName);
    }
}
