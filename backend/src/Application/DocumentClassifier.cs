using System.Text.RegularExpressions;

namespace DocuArchive.Application;

/// <summary>One classification outcome. A suggestion, not a fact — the UI must show it as such.</summary>
public record ClassificationResult(
    string Category,
    double Confidence,
    string Reason,
    bool NeedsReview);

/// <summary>
/// Pure document classification from OCR text (no I/O, no AI, no external service).
/// Scores six categories with weighted keyword / regex signals and returns the winner
/// with a confidence score. Rules, in order:
/// <list type="bullet">
/// <item>Each signal is worth points (strong 3, medium 2, weak 1); the category with the most points wins. Ties break in a fixed priority order (Invoice, Receipt, BankStatement, Contract, Letter) so results are deterministic.</item>
/// <item>Confidence grows with the winning score: 30 + 8 points, clamped to 30..95. A close race (runner-up within 1 point) costs 15 points — a near-tie is not a confident answer.</item>
/// <item>A document needs human review when confidence is below <see cref="ReviewThreshold"/> (60) or when nothing matched and the category is "Other". Low-confidence documents are never silently filed under the wrong category.</item>
/// </list>
/// </summary>
public static class DocumentClassifier
{
    public static readonly string[] Categories =
        ["Receipt", "Invoice", "Contract", "BankStatement", "Letter", "Other"];

    /// <summary>Below this confidence a document goes to the human-review queue.</summary>
    public const double ReviewThreshold = 60;

    public static bool IsKnownCategory(string? category)
        => category is not null && Categories.Contains(category, StringComparer.Ordinal);

    private sealed record Signal(int Points, Regex Pattern, string Label);

    private static Regex Word(string word, RegexOptions options = RegexOptions.IgnoreCase)
        => new(@"\b" + Regex.Escape(word) + @"\b", options | RegexOptions.Compiled);

    private static Regex Phrase(string phrase)
        => new(Regex.Escape(phrase), RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, Signal[]> Signals = new()
    {
        ["Invoice"] =
        [
            new(3, new Regex(@"invoice\s*(no\.?|number|#|num)\s*:?\s*[A-Z0-9][A-Z0-9\-/]*", RegexOptions.IgnoreCase | RegexOptions.Compiled), "invoice number"),
            new(3, Phrase("tax invoice"), "tax invoice"),
            new(2, Word("invoice"), "invoice"),
            new(2, Phrase("payment due"), "payment due"),
            new(2, Phrase("amount due"), "amount due"),
            new(2, Phrase("due date"), "due date"),
            new(2, Phrase("bill to"), "bill to"),
            new(2, Phrase("invoice date"), "invoice date"),
            new(1, Phrase("vat no"), "VAT number"),
            new(1, Phrase("purchase order"), "purchase order"),
            new(1, new Regex(@"p\.?o\.?\s*(no\.?|number|#)\s*:?\s*[A-Z0-9][A-Z0-9\-/]*", RegexOptions.IgnoreCase | RegexOptions.Compiled), "PO number"),
        ],
        ["Receipt"] =
        [
            new(3, Word("receipt"), "receipt"),
            new(2, Word("total"), "total"),
            new(2, Word("subtotal"), "subtotal"),
            new(2, Word("vat"), "VAT"),
            new(2, Word("till"), "till"),
            new(2, Phrase("change due"), "change due"),
            new(2, new Regex(@"card\s*\*+\s*\d{4}", RegexOptions.IgnoreCase | RegexOptions.Compiled), "card payment"),
            new(1, Word("paid"), "paid"),
            new(1, Phrase("thank you"), "thank you"),
        ],
        ["Contract"] =
        [
            new(3, Word("contract"), "contract"),
            new(3, Word("agreement"), "agreement"),
            new(2, Word("hereby"), "hereby"),
            new(2, Word("clause"), "clause"),
            new(2, Phrase("terms and conditions"), "terms and conditions"),
            new(2, Phrase("governing law"), "governing law"),
            new(2, Phrase("effective date"), "effective date"),
            new(2, Word("termination"), "termination"),
            new(2, Phrase("party of the first part"), "party of the first part"),
            new(1, Word("signature"), "signature"),
            new(1, Word("signed"), "signed"),
        ],
        ["BankStatement"] =
        [
            new(3, Phrase("bank statement"), "bank statement"),
            new(3, Phrase("sort code"), "sort code"),
            new(3, Phrase("statement period"), "statement period"),
            new(2, Phrase("account number"), "account number"),
            new(2, Phrase("opening balance"), "opening balance"),
            new(2, Phrase("closing balance"), "closing balance"),
            new(2, Phrase("balance brought forward"), "balance brought forward"),
            new(2, Phrase("direct debit"), "direct debit"),
            new(2, Phrase("standing order"), "standing order"),
            new(2, Word("iban"), "IBAN"),
            new(1, Word("transactions"), "transactions"),
        ],
        ["Letter"] =
        [
            new(3, Phrase("dear sir"), "dear sir"),
            new(3, Phrase("dear madam"), "dear madam"),
            new(3, new Regex(@"dear\s+(mr|mrs|ms|miss|dr)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "dear + title"),
            new(2, Phrase("yours sincerely"), "yours sincerely"),
            new(2, Phrase("yours faithfully"), "yours faithfully"),
            new(2, Phrase("kind regards"), "kind regards"),
            new(2, Phrase("to whom it may concern"), "to whom it may concern"),
            new(1, new Regex(@"^dear\s+[A-Z]", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled), "dear + name"),
        ],
    };

    private static readonly string[] PriorityOrder =
        ["Invoice", "Receipt", "BankStatement", "Contract", "Letter"];

    public static ClassificationResult Classify(string? ocrText, string? vendor = null, decimal? total = null)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return new ClassificationResult("Other", 30, "No text was extracted, so no category could be determined.", true);

        var scores = new Dictionary<string, (int Points, List<string> Matched)>();
        foreach (var category in PriorityOrder)
        {
            var points = 0;
            var matched = new List<string>();
            foreach (var signal in Signals[category])
            {
                if (!signal.Pattern.IsMatch(ocrText)) continue;
                points += signal.Points;
                matched.Add(signal.Label);
            }
            scores[category] = (points, matched);
        }

        // Receipts get a bonus only when the extraction found a vendor and a total:
        // a till receipt is a business name plus amounts, not just the word "total".
        if (vendor is not null && total is not null)
        {
            var (points, matched) = scores["Receipt"];
            scores["Receipt"] = (points + 2, matched);
        }

        var winner = PriorityOrder
            .OrderByDescending(c => scores[c].Points)
            .First();
        var winnerPoints = scores[winner].Points;
        var runnerUpPoints = PriorityOrder
            .Where(c => c != winner)
            .Max(c => scores[c].Points);

        if (winnerPoints == 0)
            return new ClassificationResult("Other", 30, "No category signals matched the text.", true);

        var confidence = Math.Clamp(30 + 8 * winnerPoints, 30, 95);
        var closeRace = runnerUpPoints > 0 && winnerPoints - runnerUpPoints <= 1;
        if (closeRace) confidence = Math.Max(30, confidence - 15);

        var matchedLabels = scores[winner].Matched;
        var reason = matchedLabels.Count > 0
            ? $"Matched {winner} signals: {string.Join(", ", matchedLabels)} (score {winnerPoints})."
            : $"Weak {winner} match (score {winnerPoints}).";
        if (closeRace)
            reason += " Another category scored almost as highly, so this needs a human check.";

        var needsReview = confidence < ReviewThreshold;
        return new ClassificationResult(winner, confidence, reason, needsReview);
    }
}
