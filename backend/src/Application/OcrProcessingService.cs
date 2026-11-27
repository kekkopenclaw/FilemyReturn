using DocuArchive.Domain;

namespace DocuArchive.Application;

/// <summary>
/// OCR processing use case (Phase 3): runs the registered <see cref="IOcrEngine"/> over a
/// document's stored file, extracts receipt fields from the text, and moves the document to
/// Ready (OCR completed) or Failed (engine could not read it). The claim step is atomic
/// (<see cref="IDocumentRepository.TryClaimForOcrAsync"/>) so two workers can never process
/// the same document twice. Reprocess resets a document to Queued so the worker picks it up
/// again; nothing is ever re-processed silently.
/// </summary>
public class OcrProcessingService
{
    private readonly IDocumentRepository _documents;
    private readonly IDocumentStorage _storage;
    private readonly IOcrEngine _ocr;
    private readonly IAuditService _audit;
    private readonly IWorkspaceAccessService _access;
    private readonly IOcrJobQueue? _queue;
    private readonly FilingRuleService _rules;

    public OcrProcessingService(
        IDocumentRepository documents,
        IDocumentStorage storage,
        IOcrEngine ocr,
        IAuditService audit,
        IWorkspaceAccessService access,
        FilingRuleService rules,
        IOcrJobQueue? queue = null)
    {
        _documents = documents; _storage = storage; _ocr = ocr;
        _audit = audit; _access = access; _rules = rules; _queue = queue;
    }

    /// <summary>
    /// Processes one queued document: claims it, runs OCR, extracts receipt fields, stores the
    /// result and marks it Ready; on any OCR failure marks it Failed (never invents text).
    /// Returns false when the document was already claimed by another worker or is not queued.
    /// </summary>
    public async Task<bool> ProcessAsync(Guid workspaceId, Guid documentId, string processedByUserId, CancellationToken ct = default)
    {
        if (!await _documents.TryClaimForOcrAsync(workspaceId, documentId, ct)) return false;

        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct)
            ?? throw new NotFoundException("Document not found.");
        try
        {
            var result = await _ocr.ExtractTextAsync(doc.StorageKey, ct);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            {
                doc.Status = DocumentStatus.Failed;
                doc.OcrStatus = OcrStatus.Failed;
                await _documents.UpdateAsync(doc, ct);
                await _audit.LogAsync(processedByUserId, workspaceId, "Document.OcrFailed", "Document", doc.Id.ToString(),
                    "OCR produced no text for " + doc.OriginalFileName, ct);
                return true;
            }

            var fields = ReceiptFieldExtractor.Extract(result.Text);
            var classification = DocumentClassifier.Classify(result.Text, fields.Vendor, fields.Total);
            doc.OcrText = result.Text;
            doc.OcrConfidence = result.Confidence;
            doc.ExtractedVendor = fields.Vendor;
            doc.ExtractedDate = fields.Date;
            doc.ExtractedTotal = fields.Total;
            doc.ExtractedCurrency = fields.Currency;
            doc.SuggestedCategory = classification.Category;
            doc.ClassificationConfidence = classification.Confidence;
            doc.ClassificationReason = classification.Reason;
            doc.NeedsReview = classification.NeedsReview;
            doc.Status = DocumentStatus.Ready;
            doc.OcrStatus = OcrStatus.Completed;
            await _documents.UpdateAsync(doc, ct);
            await _audit.LogAsync(processedByUserId, workspaceId, "Document.OcrCompleted", "Document", doc.Id.ToString(),
                $"OCR completed for {doc.OriginalFileName} (vendor: {fields.Vendor ?? "—"}, total: {fields.Currency}{fields.Total?.ToString("0.00") ?? "—"}, category: {classification.Category} {classification.Confidence:0}%{(classification.NeedsReview ? ", needs review" : "")})", ct);
            // Phase 4 filing rules: file the document automatically. A stale rule
            // must never fail a Ready document, so evaluation failures are caught
            // and audited here (the evaluator itself already skips stale references).
            try
            {
                await _rules.EvaluateAsync(processedByUserId, workspaceId, doc, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _audit.LogAsync(processedByUserId, workspaceId, "Document.RuleEvaluationFailed", "Document", doc.Id.ToString(),
                    $"Filing-rule evaluation failed for {doc.OriginalFileName}: {ex.GetType().Name}", ct);
            }
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Engine threw (missing binary, timeout, unreadable file): mark Failed, never crash the worker.
            doc.Status = DocumentStatus.Failed;
            doc.OcrStatus = OcrStatus.Failed;
            await _documents.UpdateAsync(doc, ct);
            await _audit.LogAsync(processedByUserId, workspaceId, "Document.OcrFailed", "Document", doc.Id.ToString(),
                $"OCR failed for {doc.OriginalFileName}: {ex.GetType().Name}", ct);
            return true;
        }
    }

    /// <summary>
    /// Reprocess path: a member (StandardUser+) resets a Ready/Failed document to Queued so the
    /// worker runs OCR again. Clears the previous extraction so stale fields are never shown as current.
    /// </summary>
    public async Task<DocumentDto> ReprocessAsync(string userId, Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireRoleAsync(workspaceId, userId, WorkspaceRole.StandardUser, ct);
        var doc = await _documents.GetByIdAsync(workspaceId, documentId, ct)
            ?? throw new NotFoundException("Document not found.");
        if (doc.OcrStatus is OcrStatus.Queued or OcrStatus.Processing)
            throw new ValidationException("This document is already queued for OCR.");
        if (await _storage.OpenReadAsync(doc.StorageKey, ct) is null)
            throw new NotFoundException("Stored file not found.");

        doc.Status = DocumentStatus.Uploaded;
        doc.OcrStatus = OcrStatus.Queued;
        doc.OcrText = null;
        doc.OcrConfidence = null;
        doc.ExtractedVendor = null;
        doc.ExtractedDate = null;
        doc.ExtractedTotal = null;
        doc.ExtractedCurrency = null;
        doc.SuggestedCategory = null;
        doc.ClassificationConfidence = null;
        doc.ClassificationReason = null;
        doc.NeedsReview = false;
        doc.ConfirmedCategory = null;
        await _documents.UpdateAsync(doc, ct);
        await _audit.LogAsync(userId, workspaceId, "Document.OcrReprocessRequested", "Document", doc.Id.ToString(),
            doc.OriginalFileName, ct);
        if (_queue is not null)
            await _queue.EnqueueAsync(new OcrJob(doc.Id, workspaceId, doc.StorageKey), ct);
        return DocumentDtos.ToDto(doc);
    }
}

/// <summary>Shared document DTO mapping (kept in one place so list/get/upload/reprocess all return the same shape).</summary>
internal static class DocumentDtos
{
    public static DocumentDto ToDto(Document d) => new(
        d.Id, d.WorkspaceId, d.ArchiveId, d.OriginalFileName, d.Sha256Hash,
        d.FileSizeBytes, d.MimeType, d.Status.ToString(), d.OcrStatus.ToString(),
        d.CreatedAt, d.CreatedById,
        d.OcrText, d.OcrConfidence, d.ExtractedVendor, d.ExtractedDate,
        d.ExtractedTotal, d.ExtractedCurrency, d.SuggestedCategory,
        d.ClassificationConfidence, d.ClassificationReason, d.NeedsReview, d.ConfirmedCategory,
        // Phase 4 tags — the navigation is loaded by the repository's list/get queries;
        // anything that builds a Document without loading it (upload, OCR claim) gets [].
        (d.DocumentTags ?? []).Where(dt => dt.Tag is not null)
            .Select(dt => new TagDto(dt.Tag!.Id, dt.Tag.WorkspaceId, dt.Tag.Name, dt.Tag.Colour ?? TagService.DefaultColour(dt.Tag.Name), dt.Tag.CreatedAt))
            .OrderBy(t => t.Name).ToList(),
        // Phase 7 physical archive — the navigation is loaded by the repository's list/get queries.
        d.PhysicalBoxId, d.PhysicalBox?.Name);
}
