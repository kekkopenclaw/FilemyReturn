using System.Security.Cryptography;
using DocuArchive.Domain;
using Microsoft.Extensions.Options;

namespace DocuArchive.Application;

/// <summary>
/// Document upload use case (Phase 2, Phase 3 queues OCR). Validates the file by magic bytes (never trusting the
/// client-supplied content type), enforces the size limit, rejects exact duplicates per
/// workspace by SHA-256 (409), stores the bytes via <see cref="IDocumentStorage"/> and
/// creates the <see cref="Document"/> row (Status Uploaded, OcrStatus Queued — the OCR worker picks it up).
/// </summary>
public class DocumentUploadService
{
    private readonly IDocumentRepository _documents;
    private readonly IArchiveRepository _archives;
    private readonly IWorkspaceAccessService _access;
    private readonly IDocumentStorage _storage;
    private readonly IAuditService _audit;
    private readonly StorageSettings _settings;
    private readonly IOcrJobQueue? _ocrQueue;

    public DocumentUploadService(
        IDocumentRepository documents,
        IArchiveRepository archives,
        IWorkspaceAccessService access,
        IDocumentStorage storage,
        IAuditService audit,
        IOptions<StorageSettings> settings,
        IOcrJobQueue? ocrQueue = null)
    {
        _documents = documents; _archives = archives; _access = access;
        _storage = storage; _audit = audit; _settings = settings.Value; _ocrQueue = ocrQueue;
    }

    /// <summary>Validates and stores an upload. Returns the created document.</summary>
    /// <exception cref="ValidationException">Empty file or disallowed file type.</exception>
    /// <exception cref="PayloadTooLargeException">File exceeds <see cref="StorageSettings.MaxFileSizeBytes"/>.</exception>
    /// <exception cref="DuplicateDocumentException">An identical file (same SHA-256) already exists in this workspace.</exception>
    public async Task<DocumentDto> UploadAsync(
        string userId, Guid workspaceId, Guid? archiveId,
        string originalFileName, Stream content, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        if (archiveId.HasValue)
        {
            _ = await _archives.GetByIdAsync(workspaceId, archiveId.Value, ct)
                ?? throw new NotFoundException("Archive not found.");
        }
        if (string.IsNullOrWhiteSpace(originalFileName)) throw new ValidationException("A file name is required.");

        // Read fully (bounded by the size limit) so we can hash + sniff magic bytes + store.
        using var buffer = new MemoryStream();
        var remaining = _settings.MaxFileSizeBytes + 1;
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            remaining -= read;
            if (remaining < 0) throw new PayloadTooLargeException(
                $"File exceeds the maximum upload size of {_settings.MaxFileSizeBytes} bytes.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length == 0) throw new ValidationException("The uploaded file is empty.");

        var mimeType = FileValidation.DetectMimeType(bytes.Take(FileValidation.SignatureLength).ToArray())
            ?? throw new ValidationException("File type is not allowed. Allowed types: PDF, PNG, JPEG, GIF, TIFF, BMP, WebP.");
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));

        var duplicate = await _documents.GetByHashAsync(workspaceId, sha256, ct);
        if (duplicate is not null) throw new DuplicateDocumentException(duplicate.Id, duplicate.OriginalFileName);

        buffer.Position = 0;
        var storageKey = await _storage.SaveAsync(buffer, originalFileName, mimeType, ct);
        var document = new Document
        {
            WorkspaceId = workspaceId,
            ArchiveId = archiveId,
            OriginalFileName = Path.GetFileName(originalFileName),
            StorageKey = storageKey,
            Sha256Hash = sha256,
            FileSizeBytes = bytes.Length,
            MimeType = mimeType,
            Status = DocumentStatus.Uploaded,
            OcrStatus = OcrStatus.Queued,
            CreatedById = userId
        };
        await _documents.AddAsync(document, ct);
        await _audit.LogAsync(userId, workspaceId, "Document.Uploaded", "Document", document.Id.ToString(),
            $"{document.OriginalFileName} ({document.FileSizeBytes} bytes, {mimeType})", ct);
        // Phase 3: the upload queues OCR. The Queued status in the database is the durable
        // trigger (the polling worker picks it up); the in-memory queue, when registered, is
        // only a fast wake-up signal. The queue is optional so the API still starts before
        // the WebApi host registers it (see docs/OCR.md).
        if (_ocrQueue is not null)
            await _ocrQueue.EnqueueAsync(new OcrJob(document.Id, workspaceId, document.StorageKey), ct);
        return DocumentDtos.ToDto(document);
    }
}

/// <summary>413 Payload Too Large.</summary>
public class PayloadTooLargeException : AppException
{
    public PayloadTooLargeException(string message) : base(413, message) { }
}

/// <summary>409 Conflict: an identical file already exists in this workspace.</summary>
public class DuplicateDocumentException : AppException
{
    public DuplicateDocumentException(Guid existingDocumentId, string existingFileName)
        : base(409, $"An identical file already exists in this workspace: {existingFileName}.")
    {
        ExistingDocumentId = existingDocumentId;
    }
    public Guid ExistingDocumentId { get; }
}
