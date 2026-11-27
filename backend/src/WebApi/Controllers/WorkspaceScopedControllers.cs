using System.Net.Http.Headers;
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

/// <summary>Tag endpoints, nested under a workspace (Phase 4 organisation). Thin controller — logic lives in <see cref="TagService"/> (Application).</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/tags")]
[Authorize]
[EnableRateLimiting("default")]
public class TagsController : ControllerBase
{
    private readonly TagService _tags;
    public TagsController(TagService tags) => _tags = tags;

    [HttpGet]
    public async Task<ActionResult<List<TagDto>>> List(Guid workspaceId, CancellationToken ct)
        => Ok(await _tags.ListAsync(ControllerUser.Id(this), workspaceId, ct));

    /// <summary>Creates a tag. ReadOnly members cannot create (StandardUser+ required). 400 on a duplicate name or a bad colour.</summary>
    [HttpPost]
    public async Task<ActionResult<TagDto>> Create(Guid workspaceId, [FromBody] CreateTagRequest request, CancellationToken ct)
    {
        var tag = await _tags.CreateAsync(ControllerUser.Id(this), workspaceId, request, ct);
        return CreatedAtAction(nameof(List), new { workspaceId }, tag);
    }

    /// <summary>Deletes a tag (and all its document assignments). StandardUser+ required.</summary>
    [HttpDelete("{tagId:guid}")]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid tagId, CancellationToken ct)
    {
        await _tags.DeleteAsync(ControllerUser.Id(this), workspaceId, tagId, ct);
        return NoContent();
    }
}

/// <summary>Filing-rule endpoints, nested under a workspace (Phase 4 organisation). Thin controller — logic lives in <see cref="FilingRuleService"/> (Application).</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/filing-rules")]
[Authorize]
[EnableRateLimiting("default")]
public class FilingRulesController : ControllerBase
{
    private readonly FilingRuleService _rules;
    public FilingRulesController(FilingRuleService rules) => _rules = rules;

    [HttpGet]
    public async Task<ActionResult<List<FilingRuleDto>>> List(Guid workspaceId, CancellationToken ct)
        => Ok(await _rules.ListAsync(ControllerUser.Id(this), workspaceId, ct));

    [HttpGet("{ruleId:guid}")]
    public async Task<ActionResult<FilingRuleDto>> Get(Guid workspaceId, Guid ruleId, CancellationToken ct)
        => Ok(await _rules.GetAsync(ControllerUser.Id(this), workspaceId, ruleId, ct));

    /// <summary>Creates a filing rule. ReadOnly members cannot create (StandardUser+ required). 400 on a duplicate name, no condition, no action, or an unknown category.</summary>
    [HttpPost]
    public async Task<ActionResult<FilingRuleDto>> Create(Guid workspaceId, [FromBody] CreateFilingRuleRequest request, CancellationToken ct)
    {
        var rule = await _rules.CreateAsync(ControllerUser.Id(this), workspaceId, request, ct);
        return CreatedAtAction(nameof(Get), new { workspaceId, ruleId = rule.Id }, rule);
    }

    /// <summary>Updates a filing rule (including enabling/disabling it). StandardUser+ required.</summary>
    [HttpPut("{ruleId:guid}")]
    public async Task<ActionResult<FilingRuleDto>> Update(Guid workspaceId, Guid ruleId, [FromBody] UpdateFilingRuleRequest request, CancellationToken ct)
        => Ok(await _rules.UpdateAsync(ControllerUser.Id(this), workspaceId, ruleId, request, ct));

    /// <summary>Deletes a filing rule. StandardUser+ required.</summary>
    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid ruleId, CancellationToken ct)
    {
        await _rules.DeleteAsync(ControllerUser.Id(this), workspaceId, ruleId, ct);
        return NoContent();
    }
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
    public async Task<ActionResult<List<DocumentDto>>> List(Guid workspaceId, [FromQuery] Guid? archiveId, [FromQuery] string? search, [FromQuery] bool? needsReview, [FromQuery] Guid? tagId, CancellationToken ct)
        => Ok(await _documents.ListAsync(ControllerUser.Id(this), workspaceId, archiveId, ct, search, needsReview, tagId));

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

    /// <summary>In-browser preview (Phase 6 viewer, members only). Same bytes as download,
    /// but served with <c>Content-Disposition: inline</c> so the browser renders PDFs and
    /// images in place instead of saving them. 400 for types browsers cannot render inline.</summary>
    [HttpGet("{documentId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid workspaceId, Guid documentId, CancellationToken ct)
    {
        var (doc, stream) = await _documents.GetPreviewAsync(ControllerUser.Id(this), workspaceId, documentId, ct);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileNameStar = doc.OriginalFileName
        }.ToString();
        return File(stream, doc.MimeType);
    }

    /// <summary>
    /// Re-runs OCR on a document (Phase 3). StandardUser+ only. Resets the document to Queued,
    /// clears the previous extraction, and the OCR worker picks it up again. Returns 400 when
    /// the document is already queued/processing, 404 when the stored file is gone.
    /// </summary>
    [HttpPost("{documentId:guid}/reprocess")]
    public async Task<ActionResult<DocumentDto>> Reprocess(Guid workspaceId, Guid documentId, CancellationToken ct)
        => Ok(await _ocr.ReprocessAsync(ControllerUser.Id(this), workspaceId, documentId, ct));

    /// <summary>
    /// Human review: confirms or corrects a document's category (Phase 3 classification).
    /// StandardUser+ only. Body: { "category": "Invoice" }. Clears the review flag.
    /// Returns 400 for an unknown category.
    /// </summary>
    [HttpPost("{documentId:guid}/confirm-category")]
    public async Task<ActionResult<DocumentDto>> ConfirmCategory(Guid workspaceId, Guid documentId, [FromBody] ConfirmCategoryRequest request, CancellationToken ct)
        => Ok(await _documents.ConfirmCategoryAsync(ControllerUser.Id(this), workspaceId, documentId, request.Category, ct));

    /// <summary>
    /// Tags assigned to a document (Phase 4). Members only.
    /// </summary>
    [HttpGet("{documentId:guid}/tags")]
    public async Task<ActionResult<List<TagDto>>> ListTags(Guid workspaceId, Guid documentId, [FromServices] TagService tags, CancellationToken ct)
        => Ok(await tags.ListDocumentTagsAsync(ControllerUser.Id(this), workspaceId, documentId, ct));

    /// <summary>
    /// Assigns a tag to a document (Phase 4). StandardUser+ only. Idempotent — assigning
    /// a tag that is already there returns the current tag list. Returns 404 when the
    /// document or the tag does not exist in this workspace.
    /// </summary>
    [HttpPost("{documentId:guid}/tags")]
    public async Task<ActionResult<List<TagDto>>> AssignTag(Guid workspaceId, Guid documentId, [FromBody] AssignTagRequest request, [FromServices] TagService tags, CancellationToken ct)
        => Ok(await tags.AssignAsync(ControllerUser.Id(this), workspaceId, documentId, request.TagId, ct));

    /// <summary>
    /// Removes a tag from a document (Phase 4). StandardUser+ only. Idempotent — removing
    /// a tag that is not there is a no-op.
    /// </summary>
    [HttpDelete("{documentId:guid}/tags/{tagId:guid}")]
    public async Task<ActionResult<List<TagDto>>> UnassignTag(Guid workspaceId, Guid documentId, Guid tagId, [FromServices] TagService tags, CancellationToken ct)
        => Ok(await tags.UnassignAsync(ControllerUser.Id(this), workspaceId, documentId, tagId, ct));

    /// <summary>
    /// Manually runs the workspace's filing rules against one document (Phase 4).
    /// StandardUser+ only. Use it after creating a new rule, to file documents
    /// that were uploaded before the rule existed. Works on any document, even
    /// before OCR has run (file-name conditions can still match). Returns the
    /// updated document (tags included).
    /// </summary>
    [HttpPost("{documentId:guid}/apply-rules")]
    public async Task<ActionResult<DocumentDto>> ApplyRules(Guid workspaceId, Guid documentId, [FromServices] FilingRuleService rules, CancellationToken ct)
        => Ok(await rules.ApplyToDocumentAsync(ControllerUser.Id(this), workspaceId, documentId, ct));
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
