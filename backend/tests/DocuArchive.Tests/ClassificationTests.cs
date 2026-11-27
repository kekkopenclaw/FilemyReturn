using DocuArchive.Application;

namespace DocuArchive.Tests;

/// <summary>
/// Phase 3 classification tests (pure, no I/O): the rule + regex classifier must
/// recognise Invoice, Contract, Bank Statement, Letter and Receipt from OCR text,
/// score confidence, and send low-confidence / unmatched documents to review.
/// </summary>
public class ClassificationTests
{
    [Fact]
    public void Classify_InvoiceText_ReturnsInvoiceWithHighConfidence()
    {
        const string text = """
            ACME SUPPLIES LTD
            TAX INVOICE
            Invoice No: INV-2026-1042
            Invoice Date: 12 Nov 2026
            Bill To: Rubino Digital
            Widgets x10              £120.00
            Delivery                  £8.50
            Amount Due              £128.50
            Payment due within 30 days
            """;
        var result = DocumentClassifier.Classify(text, "ACME SUPPLIES LTD", 128.50m);
        Assert.Equal("Invoice", result.Category);
        Assert.True(result.Confidence >= 60, $"confidence was {result.Confidence}");
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_ReceiptText_ReturnsReceipt()
    {
        const string text = """
            THE CORNER CAFE
            12 Market Street, London
            Flat White              3.40
            Croissant               2.85
            Subtotal               6.25
            VAT 20%                 1.25
            TOTAL                 £7.50
            Card **** 4242
            Thank you - see you soon!
            """;
        var result = DocumentClassifier.Classify(text, "THE CORNER CAFE", 7.50m);
        Assert.Equal("Receipt", result.Category);
        Assert.True(result.Confidence >= 60, $"confidence was {result.Confidence}");
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_ContractText_ReturnsContract()
    {
        const string text = """
            SERVICE AGREEMENT
            This agreement is made on 1 January 2026 between Alpha Ltd and Beta Ltd.
            Clause 1: The parties hereby agree to the terms and conditions set out below.
            Clause 2: Termination. Either party may terminate this agreement with 30 days notice.
            Governing law: the laws of England and Wales.
            Signed: ____________________  Date: 01/01/2026
            """;
        var result = DocumentClassifier.Classify(text);
        Assert.Equal("Contract", result.Category);
        Assert.True(result.Confidence >= 60, $"confidence was {result.Confidence}");
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_BankStatementText_ReturnsBankStatement()
    {
        const string text = """
            FIRST NATIONAL BANK
            Bank Statement
            Statement Period: 01 Nov 2026 - 30 Nov 2026
            Account Number: 12345678
            Sort Code: 12-34-56
            Opening Balance: £1,240.00
            03 Nov  Direct Debit - Energy Co      -£85.00
            15 Nov  Standing Order - Rent         -£650.00
            Closing Balance: £505.00
            """;
        var result = DocumentClassifier.Classify(text);
        Assert.Equal("BankStatement", result.Category);
        Assert.True(result.Confidence >= 60, $"confidence was {result.Confidence}");
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_LetterText_ReturnsLetter()
    {
        const string text = """
            Dear Mr Rubino,
            Thank you for your email of 10 November. We are writing to confirm
            your appointment on 3 December 2026.
            Yours sincerely,
            Jane Smith
            Customer Services
            """;
        var result = DocumentClassifier.Classify(text);
        Assert.Equal("Letter", result.Category);
        Assert.True(result.Confidence >= 60, $"confidence was {result.Confidence}");
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Classify_GibberishText_ReturnsOtherAndNeedsReview()
    {
        var result = DocumentClassifier.Classify("xkq zztop 12345 !!! qwerty asdf");
        Assert.Equal("Other", result.Category);
        Assert.True(result.NeedsReview);
        Assert.True(result.Confidence < DocumentClassifier.ReviewThreshold);
    }

    [Fact]
    public void Classify_EmptyText_ReturnsOtherAndNeedsReview()
    {
        var result = DocumentClassifier.Classify("   \n\n  ");
        Assert.Equal("Other", result.Category);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Classify_OneWeakKeyword_StaysBelowThresholdAndNeedsReview()
    {
        // A single weak signal must not file the document confidently.
        var result = DocumentClassifier.Classify("Please find the total attached.");
        Assert.True(result.NeedsReview);
        Assert.True(result.Confidence < DocumentClassifier.ReviewThreshold);
    }

    [Fact]
    public void Classify_CloseRace_LowersConfidenceAndNeedsReview()
    {
        // Invoice and receipt signals tie: the classifier must admit doubt.
        const string text = "INVOICE\nReceipt";
        var result = DocumentClassifier.Classify(text);
        Assert.True(result.NeedsReview, $"expected review, got {result.Category} {result.Confidence}");
    }

    [Fact]
    public void IsKnownCategory_AcceptsOnlyTheSixCategories()
    {
        foreach (var c in DocumentClassifier.Categories) Assert.True(DocumentClassifier.IsKnownCategory(c));
        Assert.False(DocumentClassifier.IsKnownCategory("TaxReturn"));
        Assert.False(DocumentClassifier.IsKnownCategory(""));
        Assert.False(DocumentClassifier.IsKnownCategory(null));
    }
}
