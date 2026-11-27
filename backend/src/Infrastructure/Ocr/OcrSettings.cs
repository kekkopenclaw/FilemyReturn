using Microsoft.Extensions.Options;

namespace DocuArchive.Infrastructure.Ocr;

/// <summary>OCR settings, bound from the <c>Ocr</c> configuration section. All paths default to the tools on PATH.</summary>
public class OcrSettings
{
    /// <summary>Tesseract binary name/path. Default "tesseract".</summary>
    public string TesseractPath { get; set; } = "tesseract";
    /// <summary>pdftotext binary (embedded-text PDFs). Default "pdftotext".</summary>
    public string PdftotextPath { get; set; } = "pdftotext";
    /// <summary>pdftoppm binary (render scanned PDFs to images). Default "pdftoppm".</summary>
    public string PdftoppmPath { get; set; } = "pdftoppm";
    /// <summary>Tesseract languages. Default "eng".</summary>
    public string Languages { get; set; } = "eng";
    /// <summary>Tesseract page segmentation mode. 6 = assume a uniform block of text (receipts).</summary>
    public int PageSegmentationMode { get; set; } = 6;
    /// <summary>Per-file OCR timeout. Default 120 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
