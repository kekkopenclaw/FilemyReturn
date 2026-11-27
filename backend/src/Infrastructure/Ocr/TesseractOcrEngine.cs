using System.Diagnostics;
using System.Globalization;
using System.Text;
using DocuArchive.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuArchive.Infrastructure.Ocr;

/// <summary>
/// Local, free, CPU-only OCR via the Tesseract CLI (Apache 2.0) — the Phase 3 receipt engine.
/// Nothing is sent anywhere: the file is read from local storage, copied to a temp file, and
/// OCR runs in a subprocess on this machine. No paid API, no external service, no model download.
/// <list type="bullet">
/// <item>Images (PNG/JPEG/TIFF/BMP/GIF/WebP): one <c>tesseract … stdout --psm 6 tsv</c> call — the TSV gives both the words (rebuilt into lines) and a real mean word confidence.</item>
/// <item>PDFs: <c>pdftotext</c> first (embedded text is exact, confidence is null because nothing was OCR'd); when the PDF has no embedded text (a scan), render pages with <c>pdftoppm</c> and OCR each page.</item>
/// <item>When a binary is missing, times out, or the file cannot be read, returns <c>Success=false</c> with no text — never invented text, never a thrown exception into the worker.</item>
/// </list>
/// Requires <c>tesseract-ocr</c> and <c>poppler-utils</c> installed where the app runs
/// (Dockerfile: <c>apt-get install -y tesseract-ocr poppler-utils</c>; GitHub ubuntu runners already have both).
/// </summary>
public class TesseractOcrEngine : IOcrEngine
{
    private readonly IDocumentStorage _storage;
    private readonly OcrSettings _settings;
    private readonly ILogger<TesseractOcrEngine> _logger;

    public TesseractOcrEngine(IDocumentStorage storage, IOptions<OcrSettings> settings, ILogger<TesseractOcrEngine> logger)
    {
        _storage = storage;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OcrResult> ExtractTextAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await using var content = await _storage.OpenReadAsync(storageKey, cancellationToken);
        if (content is null)
        {
            _logger.LogWarning("OCR requested for missing storage key {StorageKey}.", storageKey);
            return new OcrResult(null, null, "eng", false);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "docuarchive-ocr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var ext = Path.GetExtension(storageKey);
            var tempFile = Path.Combine(tempDir, "input" + (string.IsNullOrEmpty(ext) ? ".bin" : ext));
            await using (var file = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await content.CopyToAsync(file, cancellationToken);

            return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? await ExtractFromPdfAsync(tempFile, tempDir, cancellationToken)
                : await ExtractFromImageAsync(tempFile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR failed for storage key {StorageKey}.", storageKey);
            return new OcrResult(null, null, "eng", false);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private async Task<OcrResult> ExtractFromImageAsync(string imagePath, CancellationToken ct)
    {
        // One TSV run gives words + confidences; text is rebuilt from the TSV lines.
        var (exit, stdout, _) = await RunAsync(_settings.TesseractPath,
            [imagePath, "stdout", "--psm", _settings.PageSegmentationMode.ToString(CultureInfo.InvariantCulture), "-l", _settings.Languages, "tsv"], ct);
        if (exit != 0) return new OcrResult(null, null, _settings.Languages, false);
        var (text, confidence) = ParseTsv(stdout);
        if (string.IsNullOrWhiteSpace(text)) return new OcrResult(null, null, _settings.Languages, false);
        return new OcrResult(text, confidence, _settings.Languages, true);
    }

    private async Task<OcrResult> ExtractFromPdfAsync(string pdfPath, string tempDir, CancellationToken ct)
    {
        var (exit, text, _) = await RunAsync(_settings.PdftotextPath, [pdfPath, "-"], ct);
        if (exit == 0 && !string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 20)
        {
            // Embedded text: exact, not OCR — confidence is null by design.
            return new OcrResult(Normalise(text), null, _settings.Languages, true);
        }

        // Scanned PDF: render pages to PNG and OCR each one.
        var (renderExit, _, _) = await RunAsync(_settings.PdftoppmPath,
            ["-png", "-r", "300", pdfPath, Path.Combine(tempDir, "page")], ct);
        if (renderExit != 0) return new OcrResult(null, null, _settings.Languages, false);

        var pages = Directory.GetFiles(tempDir, "page-*.png").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (pages.Count == 0) return new OcrResult(null, null, _settings.Languages, false);

        var allText = new StringBuilder();
        var confidences = new List<double>();
        foreach (var page in pages)
        {
            var pageResult = await ExtractFromImageAsync(page, ct);
            if (pageResult is { Success: true, Text: not null })
            {
                allText.AppendLine(pageResult.Text);
                if (pageResult.Confidence is not null) confidences.Add(pageResult.Confidence.Value);
            }
        }
        var combined = Normalise(allText.ToString());
        if (string.IsNullOrWhiteSpace(combined)) return new OcrResult(null, null, _settings.Languages, false);
        return new OcrResult(combined, confidences.Count > 0 ? confidences.Average() : null, _settings.Languages, true);
    }

    /// <summary>Rebuilds text lines from Tesseract TSV output and returns (text, mean word confidence 0-100).</summary>
    internal static (string Text, double? Confidence) ParseTsv(string tsv)
    {
        var lines = new Dictionary<(int Block, int Par, int Line), List<(int WordNum, string Text)>>();
        var confidences = new List<double>();
        foreach (var raw in tsv.Split('\n'))
        {
            var cols = raw.Split('\t');
            // level page block par line word left top width height conf text
            if (cols.Length < 12 || !int.TryParse(cols[0], out var level) || level != 5) continue;
            var text = cols[11].Trim();
            if (string.IsNullOrEmpty(text)) continue;
            // TSV columns: level page_num block_num par_num line_num word_num left top width height conf text
            _ = int.TryParse(cols[2], out var block);
            _ = int.TryParse(cols[3], out var par);
            _ = int.TryParse(cols[4], out var line);
            _ = int.TryParse(cols[5], out var wordNum);
            var key = (block, par, line);
            if (!lines.TryGetValue(key, out var words)) lines[key] = words = [];
            words.Add((wordNum, text));
            if (double.TryParse(cols[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf) && conf >= 0)
                confidences.Add(conf);
        }
        var sb = new StringBuilder();
        foreach (var words in lines.Values)
            sb.AppendLine(string.Join(" ", words.OrderBy(w => w.WordNum).Select(w => w.Text)));
        return (Normalise(sb.ToString()), confidences.Count > 0 ? confidences.Average() : null);
    }

    private static string Normalise(string text)
        => string.Join("\n", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd()));

    /// <summary>Runs a CLI tool with no shell and a hard timeout. Arguments are passed as a list, never through a shell string.</summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string tool, string[] args, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return (-1, string.Empty, "failed to start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning("OCR tool '{Tool}' is not installed or could not start: {Message}", tool, ex.Message);
            return (-1, string.Empty, ex.Message);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            _logger.LogWarning("OCR tool '{Tool}' timed out after {Timeout}s.", tool, _settings.TimeoutSeconds);
            return (-1, string.Empty, "timeout");
        }
    }
}
