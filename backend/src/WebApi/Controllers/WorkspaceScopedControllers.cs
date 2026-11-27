using DocuArchive.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuArchive.WebApi.Controllers;

/// <summary>Archive endpoints, nested under a workspace. Thin controller — logic lives in <see cref="ArchiveService"/> (Application).</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/archives")]
[Authorize]
[EnableRateLimiting("default")]
public class ArchivesController : ControllerBase
{
    private readonly ArchiveService _archives;
    public ArchivesController(ArchiveService archives) => _archives = archives;

    [HttpGet]
    public async Task<ActionResult<List<ArchiveDto>>> List(Guid workspaceId, CancellationToken ct)
        => Ok(await _archives.ListAsync(ControllerUser.Id(this), workspaceId, ct));

    /// <summary>Creates an archive. ReadOnly members cannot create (StandardUser+ required).</summary>
    [HttpPost]
    public async Task<ActionResult<ArchiveDto>> Create(Guid workspaceId, [FromBody] CreateArchiveRequest request, CancellationToken ct)
    {
        var archive = await _archives.CreateAsync(ControllerUser.Id(this), workspaceId, request, ct);
        return CreatedAtAction(nameof(Get), new { workspaceId, archiveId = archive.Id }, archive);
    }

    [HttpGet("{archiveId:guid}")]
    public async Task<ActionResult<ArchiveDto>> Get(Guid workspaceId, Guid archiveId, CancellationToken ct)
        => Ok(await _archives.GetAsync(ControllerUser.Id(this), workspaceId, archiveId, ct));
}

/// <summary>Document read/upload/download endpoints (Phase 2 adds upload + download).</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/documents")]
[Authorize]
[EnableRateLimiting("default")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documents;
    private readonly DocumentUploadService _upload;
    private readonly OcrProcessingService _ocr;
    public DocumentsController(DocumentService documents, DocumentUploadService upload, OcrProcessingService ocr)
    {
        _documents = documents; _upload = upload; _ocr = ocr;
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> List(Guid workspaceId, [FromQuery] Guid? archiveId, CancellationToken ct)
        => Ok(await _documents.ListAsync(ControllerUser.Id(this), workspaceId, archiveId, ct));

    [HttpGet("{documentId:guid}")]
    public async Task<ActionResult<DocumentDto>> Get(Guid workspaceId, Guid documentId, CancellationToken ct)
        => Ok(await _documents.GetAsync(ControllerUser.Id(this), workspaceId, documentId, ct));

    /// <summary>
    /// Uploads a document (multipart/form-data, field <c>file</c>; optional <c>?archiveId=</c>).
    /// StandardUser+ only. Validates magic bytes (PDF/PNG/JPEG/GIF/TIFF/BMP/WebP), size limit
    /// (25 MiB default) and rejects exact duplicates per workspace (409).
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(52_428_800)] // 50 MiB request cap; the service enforces the file limit with a clean 413.
    public async Task<ActionResult<DocumentDto>> Upload(Guid workspaceId, [FromForm] IFormFile? file, [FromQuery] Guid? archiveId, CancellationToken ct)
    {
        if (file is null) throw new ValidationException("A file is required (multipart field 'file').");
        await using var stream = file.OpenReadStream();
        var doc = await _upload.UploadAsync(ControllerUser.Id(this), workspaceId, archiveId, file.FileName, stream, ct);
        return CreatedAtAction(nameof(Get), new { workspaceId, documentId = doc.Id }, doc);
    }

    /// <summary>Securely downloads a document's stored bytes (members only).</summary>
    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        var (doc, stream) = await _documents.GetDownloadAsync(ControllerUser.Id(this), workspaceId, documentId, ct);
        return File(stream, doc.MimeType, doc.OriginalFileName);
    }

    /// <summary>
    /// Re-runs OCR on a document (Phase 3). StandardUser+ only. Resets the document to Queued,
    /// clears the previous extraction, and the OCR worker picks it up again. Returns 400 when
    /// the document is already queued/processing, 404 when the stored file is gone.
    /// </summary>
    [HttpPost("{documentId:guid}/reprocess")]
    public async Task<ActionResult<DocumentDto>> Reprocess(Guid workspaceId, Guid documentId, CancellationToken ct)
        => Ok(await _ocr.ReprocessAsync(ControllerUser.Id(this), workspaceId, documentId, ct));
}

/// <summary>Upload history for a workspace (newest first, with uploader).</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/uploads")]
[Authorize]
[EnableRateLimiting("default")]
public class UploadHistoryController : ControllerBase
{
    private readonly DocumentService _documents;
    public UploadHistoryController(DocumentService documents) => _documents = documents;

    [HttpGet]
    public async Task<ActionResult<List<UploadHistoryDto>>> List(Guid workspaceId, CancellationToken ct)
        => Ok(await _documents.ListUploadHistoryAsync(ControllerUser.Id(this), workspaceId, ct));
}

/// <summary>Dashboard counts from the real database (0 when empty).</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/dashboard")]
[Authorize]
[EnableRateLimiting("default")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboard;
    public DashboardController(DashboardService dashboard) => _dashboard = dashboard;

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(Guid workspaceId, CancellationToken ct)
        => Ok(await _dashboard.GetAsync(ControllerUser.Id(this), workspaceId, ct));
}

/// <summary>Audit-log reads (ArchiveManager+ only). Read-only: no edit/delete endpoint exists.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/audit-logs")]
[Authorize]
[EnableRateLimiting("default")]
public class AuditController : ControllerBase
{
    private readonly AuditQueryService _audit;
    public AuditController(AuditQueryService audit) => _audit = audit;

    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> List(Guid workspaceId, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _audit.ListAsync(ControllerUser.Id(this), workspaceId, skip, take, ct));
}
