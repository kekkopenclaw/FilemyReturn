using DocuArchive.Domain;

namespace DocuArchive.Infrastructure.Ocr;

/// <summary>
/// PaddleOCR engine — NOT wired up. Evaluated for Phase 3 (see docs/OCR.md) and deliberately
/// not used for the receipt flow: self-hosted PaddleOCR needs a Python sidecar plus model
/// downloads (~1 GB+ RAM), which does not fit the Render Free container (512 MB) or a
/// single-process .NET deploy. <see cref="TesseractOcrEngine"/> is the engine that actually
/// runs here. This class stays as an explicit stub so nobody mistakes it for working OCR:
/// it throws instead of returning fake text.
/// </summary>
public class PaddleOcrEngine : IOcrEngine
{
    /// <inheritdoc/>
    public Task<OcrResult> ExtractTextAsync(string storageKey, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("PaddleOCR is not wired up: it needs a Python sidecar with ~1 GB+ RAM (see docs/OCR.md). Use TesseractOcrEngine, the registered IOcrEngine implementation.");
}
