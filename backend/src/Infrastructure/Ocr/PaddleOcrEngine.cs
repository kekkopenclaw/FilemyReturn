using DocuArchive.Domain;

namespace DocuArchive.Infrastructure.Ocr;

/// <summary>
/// PaddleOCR engine stub — Phase 3. Self-hosted PaddleOCR is not wired up in Phase 1;
/// this class deliberately throws instead of returning fake OCR text or invented results.
/// </summary>
public class PaddleOcrEngine : IOcrEngine
{
    /// <inheritdoc/>
    public Task<OcrResult> ExtractTextAsync(string storageKey, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("PaddleOCR is Phase 3 and is not implemented yet. No OCR text is available in Phase 1.");
}
