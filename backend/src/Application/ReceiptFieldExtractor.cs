using System.Globalization;
using System.Text.RegularExpressions;

namespace DocuArchive.Application;

/// <summary>Fields pulled out of OCR text for one receipt. All nullable: a field the text does not support is left null, never guessed.</summary>
public record ReceiptFields(
    string? Vendor,
    DateTime? Date,
    decimal? Total,
    string? Currency,
    string? SuggestedCategory,
    string? CategoryReason);

/// <summary>
/// Pure receipt-field extraction from OCR text (no I/O, no AI, no external service).
/// Rules, in order:
/// <list type="bullet">
/// <item>Vendor: the first non-empty line that looks like a business name (letters, no digits-only, not a separator line).</item>
/// <item>Date: the first date in dd MMM yyyy / dd/MM/yyyy / yyyy-MM-dd form.</item>
/// <item>Total: the amount on the line containing "TOTAL" (largest such amount when several); otherwise the largest amount on the page. Currency is the symbol next to it (£, $, €).</item>
/// <item>Category: "Receipt" is suggested only when a vendor AND a total were both found and the text contains a receipt keyword (total, vat, paid, change, till, receipt).</item>
/// </list>
/// </summary>
public static class ReceiptFieldExtractor
{
    private static readonly Regex DateDmyWords = new(
        @"\b(\d{1,2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+(\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DateDmySlashes = new(
        @"\b(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})\b",
        RegexOptions.Compiled);
    private static readonly Regex DateIso = new(
        @"\b(\d{4})-(\d{2})-(\d{2})\b",
        RegexOptions.Compiled);
    private static readonly Regex Amount = new(
        @"([£$€])\s?(\d{1,3}(?:[,\s]\d{3})*\.\d{2}|\d+\.\d{2})|(\d{1,3}(?:[,\s]\d{3})*\.\d{2})\s?([£$€])|(\d{1,3}(?:,\d{3})*\.\d{2})",
        RegexOptions.Compiled);

    private static readonly string[] ReceiptKeywords =
        ["total", "vat", "paid", "change", "till", "receipt", "subtotal", "amount due"];

    public static ReceiptFields Extract(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return new ReceiptFields(null, null, null, null, null, null);

        var lines = ocrText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var vendor = ExtractVendor(lines);
        var date = ExtractDate(ocrText);
        var (total, currency) = ExtractTotal(lines);
        var (category, reason) = SuggestCategory(lines, vendor, total);

        return new ReceiptFields(vendor, date, total, currency, category, reason);
    }

    private static string? ExtractVendor(List<string> lines)
    {
        foreach (var line in lines)
        {
            // Skip separators, dates, totals and lines that are mostly digits/punctuation.
            if (line.All(c => !char.IsLetter(c))) continue;
            if (line.Length < 3 || line.Length > 60) continue;
            if (line.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)) continue;
            if (DateDmyWords.IsMatch(line) || DateDmySlashes.IsMatch(line) || DateIso.IsMatch(line)) continue;
            if (line.StartsWith("Tel", StringComparison.OrdinalIgnoreCase)) continue;
            return line;
        }
        return null;
    }

    private static DateTime? ExtractDate(string text)
    {
        var m = DateDmyWords.Match(text);
        if (m.Success && DateTime.TryParseExact(
                $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}",
                ["d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
            return d1.Date;

        m = DateDmySlashes.Match(text);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var day)
            && int.TryParse(m.Groups[2].Value, out var month)
            && int.TryParse(m.Groups[3].Value, out var year))
        {
            try { return new DateTime(year, month, day); } catch (ArgumentOutOfRangeException) { /* fall through */ }
        }

        m = DateIso.Match(text);
        if (m.Success && DateTime.TryParseExact(m.Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d3))
            return d3.Date;

        return null;
    }

    private static (decimal? Total, string? Currency) ExtractTotal(List<string> lines)
    {
        // Prefer the amount on a TOTAL line; take the largest amount on such lines.
        decimal? best = null;
        string? currency = null;
        foreach (var line in lines.Where(l => l.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match m in Amount.Matches(line))
            {
                var (value, cur) = ParseAmount(m);
                if (value is null) continue;
                if (best is null || value > best) { best = value; currency = cur ?? currency; }
            }
        }
        if (best is not null) return (best, currency);

        // No TOTAL line: fall back to the largest amount anywhere on the page.
        foreach (var line in lines)
        {
            foreach (Match m in Amount.Matches(line))
            {
                var (value, cur) = ParseAmount(m);
                if (value is null) continue;
                if (best is null || value > best) { best = value; currency = cur ?? currency; }
            }
        }
        return (best, currency);
    }

    private static (decimal? Value, string? Currency) ParseAmount(Match m)
    {
        var symbol = m.Groups[1].Success ? m.Groups[1].Value
            : m.Groups[4].Success ? m.Groups[4].Value : null;
        var digits = m.Groups[2].Success ? m.Groups[2].Value
            : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[5].Value;
        digits = digits.Replace(",", string.Empty).Replace(" ", string.Empty);
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? (value, symbol)
            : (null, symbol);
    }

    private static (string? Category, string? Reason) SuggestCategory(List<string> lines, string? vendor, decimal? total)
    {
        if (vendor is null || total is null) return (null, null);
        var joined = string.Join(" ", lines).ToLowerInvariant();
        if (ReceiptKeywords.Any(k => joined.Contains(k, StringComparison.Ordinal)))
            return ("Receipt", "Vendor and total were extracted and the text contains receipt keywords (e.g. total/VAT).");
        return (null, null);
    }
}
