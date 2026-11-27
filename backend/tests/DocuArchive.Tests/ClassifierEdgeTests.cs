using DocuArchive.Application;

namespace DocuArchive.Tests;

/// <summary>
/// Classifier edge cases the happy-path tests do not cover: confidence maths
/// (clamp, close-race penalty, threshold boundaries), tie-breaking priority,
/// the receipt vendor+total bonus, case-insensitivity and per-category floors.
/// All expectations are computed from the documented rules in DocumentClassifier:
/// confidence = clamp(30 + 8 * points, 30, 95), minus 15 on a close race
/// (runner-up within 1 point), review below 60.
/// </summary>
public class ClassifierEdgeTests
{
    [Fact]
    public void Classify_NullText_ReturnsOther30NeedsReview()
    {
        var result = DocumentClassifier.Classify(null);
        Assert.Equal("Other", result.Category);
        Assert.Equal(30, result.Confidence);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Classify_EmptyText_ReturnsOther30NeedsReview()
    {
        var result = DocumentClassifier.Classify("");
        Assert.Equal("Other", result.Category);
        Assert.Equal(30, result.Confidence);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Classify_ReviewThreshold_Is60()
    {
        Assert.Equal(60, DocumentClassifier.ReviewThreshold);
    }

    [Fact]
    public void Classify_MaxScore_ClampsConfidenceAt95()
    {
        // Every invoice signal fires (21 points -> 30 + 168 = 198, clamped to 95).
        const string text = """
            ACME SUPPLIES LTD
            TAX INVOICE
            Invoice No: INV-2026-1042
            Invoice Date: 12 Nov 2026
            Bill To: Rubino Digital
            VAT No: GB123456789
            Purchase Order PO No: PO-778
            Amount Due £128.50
            Payment due within 30 days
            Due date: 12 Dec 2026
            """;
        var result = DocumentClassifier.Classify(text);
        Assert.Equal("Invoice", result.Category);
        Assert.Equal(95, result.Confidence);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_ReceiptBonus_VendorAndTotalLiftConfidenceAboveThreshold()
    {
        // "TOTAL" alone is 2 points (46, review). The vendor+total bonus adds 2
        // points, taking it to 4 points (62, no review) — the bonus is the difference.
        var withBonus = DocumentClassifier.Classify("TOTAL £10.00", "THE SHOP", 10.00m);
        Assert.Equal("Receipt", withBonus.Category);
        Assert.Equal(62, withBonus.Confidence);
        Assert.False(withBonus.NeedsReview);

        var withoutBonus = DocumentClassifier.Classify("TOTAL £10.00");
        Assert.Equal("Receipt", withoutBonus.Category);
        Assert.Equal(46, withoutBonus.Confidence);
        Assert.True(withoutBonus.NeedsReview);
    }

    [Fact]
    public void Classify_FourPoints_PassesThreshold_ThreePoints_DoesNot()
    {
        // 4 points -> 30 + 32 = 62 (pass). 3 points -> 30 + 24 = 54 (review).
        var four = DocumentClassifier.Classify("TOTAL £5.00\nSubtotal £4.00");
        Assert.Equal("Receipt", four.Category);
        Assert.Equal(62, four.Confidence);
        Assert.False(four.NeedsReview);

        var three = DocumentClassifier.Classify("Receipt");
        Assert.Equal("Receipt", three.Category);
        Assert.Equal(54, three.Confidence);
        Assert.True(three.NeedsReview);
    }

    [Fact]
    public void Classify_TieBreaksToInvoiceOnEqualPoints()
    {
        // Invoice 5 (invoice number 3 + "invoice" 2), Receipt 5 ("receipt" 3 +
        // "total" 2). A tie is also a close race: 30 + 40 - 15 = 55, review.
        // Priority order (Invoice first) decides the winner deterministically.
        var result = DocumentClassifier.Classify("Invoice No: INV-1\nReceipt\nTOTAL £5.00");
        Assert.Equal("Invoice", result.Category);
        Assert.Equal(55, result.Confidence);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Classify_OnePointLead_IsStillACloseRace()
    {
        // Invoice 5, Receipt 4 (subtotal 2 + total 2). A 1-point lead costs 15:
        // 30 + 40 - 15 = 55, review.
        var result = DocumentClassifier.Classify("Invoice No: INV-1\nSubtotal £4.00\nTotal £5.00");
        Assert.Equal("Invoice", result.Category);
        Assert.Equal(55, result.Confidence);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Classify_TwoPointLead_IsConfident()
    {
        // Invoice 5, Receipt 2. A 2-point lead is not a close race: 70, no review.
        var result = DocumentClassifier.Classify("Invoice No: INV-1\nTOTAL £5.00");
        Assert.Equal("Invoice", result.Category);
        Assert.Equal(70, result.Confidence);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_LowercaseInvoice_MatchesCaseInsensitively()
    {
        var result = DocumentClassifier.Classify("tax invoice\ninvoice no: inv-9\namount due £50.00");
        Assert.Equal("Invoice", result.Category);
        Assert.Equal(95, result.Confidence);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_BankStatement_SortCodeAlone_NeedsReview()
    {
        // "sort code" alone is 3 points -> 54, below the threshold.
        var result = DocumentClassifier.Classify("Sort Code: 12-34-56");
        Assert.Equal("BankStatement", result.Category);
        Assert.Equal(54, result.Confidence);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Classify_BankStatement_FullHeader_IsConfident()
    {
        var result = DocumentClassifier.Classify("""
            Bank Statement
            Statement Period: 01 Nov 2026 - 30 Nov 2026
            Sort Code: 12-34-56
            Account Number: 12345678
            """);
        Assert.Equal("BankStatement", result.Category);
        Assert.Equal(95, result.Confidence);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_Letter_DearTitlePlusSignoff_IsConfident()
    {
        // "Dear Dr" fires dear+title (3) and ^dear+[A-Z] (1); "Yours faithfully" (2):
        // 6 points -> 30 + 48 = 78, no review.
        var result = DocumentClassifier.Classify("""
            Dear Dr Smith,
            Thank you for your letter.
            Yours faithfully,
            Jane
            """);
        Assert.Equal("Letter", result.Category);
        Assert.Equal(78, result.Confidence);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_Contract_AgreementAndHereby_IsConfident()
    {
        var result = DocumentClassifier.Classify("""
            This agreement is hereby entered into by the parties.
            Clause 4: termination.
            """);
        Assert.Equal("Contract", result.Category);
        Assert.Equal(95, result.Confidence);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_Reason_NamesMatchedSignals()
    {
        var result = DocumentClassifier.Classify("TAX INVOICE\nInvoice No: INV-1");
        Assert.Contains("invoice number", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tax invoice", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
