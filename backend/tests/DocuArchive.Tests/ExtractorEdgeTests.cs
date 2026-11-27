using DocuArchive.Application;

namespace DocuArchive.Tests;

/// <summary>
/// ReceiptFieldExtractor edge cases: date formats (ISO, full month name,
/// invalid dates), total selection (multiple TOTAL lines, thousands separators,
/// currency placement), vendor rules (separators, Tel lines, length cap) and
/// the category rule (needs vendor AND total AND a receipt keyword).
/// </summary>
public class ExtractorEdgeTests
{
    [Fact]
    public void Extract_IsoDate_Parses()
    {
        var fields = ReceiptFieldExtractor.Extract("SHOP NAME\n2026-11-24\nTOTAL £10.00");
        Assert.Equal("SHOP NAME", fields.Vendor);
        Assert.Equal(new DateTime(2026, 11, 24), fields.Date);
        Assert.Equal(10.00m, fields.Total);
        Assert.Equal("£", fields.Currency);
        Assert.Equal("Receipt", fields.SuggestedCategory);
    }

    [Fact]
    public void Extract_FullMonthName_Parses()
    {
        var fields = ReceiptFieldExtractor.Extract("THE BAKERY\n24 November 2026\nTOTAL £6.20");
        Assert.Equal("THE BAKERY", fields.Vendor);
        Assert.Equal(new DateTime(2026, 11, 24), fields.Date);
        Assert.Equal(6.20m, fields.Total);
    }

    [Fact]
    public void Extract_SeptAbbreviation_Parses()
    {
        // The date regex accepts "Sept" as a month, so it must parse.
        var fields = ReceiptFieldExtractor.Extract("THE SHOP\n15 Sept 2026\nTOTAL £5.00");
        Assert.Equal(new DateTime(2026, 9, 15), fields.Date);
    }

    [Fact]
    public void Extract_InvalidSlashDate_ReturnsNullDate()
    {
        // 31 Feb does not exist: the date is left null, never guessed.
        var fields = ReceiptFieldExtractor.Extract("SHOP\n31/02/2026\nTOTAL £5.00");
        Assert.Equal("SHOP", fields.Vendor);
        Assert.Null(fields.Date);
        Assert.Equal(5.00m, fields.Total);
    }

    [Fact]
    public void Extract_MultipleTotalLines_TakesLargest()
    {
        var fields = ReceiptFieldExtractor.Extract("TOTAL £5.00\nTOTAL £12.50");
        Assert.Equal(12.50m, fields.Total);
        Assert.Equal("£", fields.Currency);
        Assert.Null(fields.Vendor); // TOTAL lines are never the vendor.
    }

    [Fact]
    public void Extract_ThousandsSeparator_Parses()
    {
        var fields = ReceiptFieldExtractor.Extract("TOTAL £1,234.56");
        Assert.Equal(1234.56m, fields.Total);
        Assert.Equal("£", fields.Currency);
    }

    [Fact]
    public void Extract_EuroCurrency_KeepsSymbol()
    {
        var fields = ReceiptFieldExtractor.Extract("CAFE\nTOTAL €9.99");
        Assert.Equal(9.99m, fields.Total);
        Assert.Equal("€", fields.Currency);
        Assert.Equal("CAFE", fields.Vendor);
        Assert.Equal("Receipt", fields.SuggestedCategory);
    }

    [Fact]
    public void Extract_AmountAfterSymbol_KeepsCurrency()
    {
        var fields = ReceiptFieldExtractor.Extract("SHOP\nTOTAL 12.50 £");
        Assert.Equal(12.50m, fields.Total);
        Assert.Equal("£", fields.Currency);
    }

    [Fact]
    public void Extract_AmountWithoutCurrency_ReturnsNullCurrency()
    {
        var fields = ReceiptFieldExtractor.Extract("SHOP\nTOTAL 18.40");
        Assert.Equal(18.40m, fields.Total);
        Assert.Null(fields.Currency);
    }

    [Fact]
    public void Extract_VendorSkipsSeparatorsAndTelLines()
    {
        var fields = ReceiptFieldExtractor.Extract("----------\nTel: 020 7946 0123\nTHE SHOP\nTOTAL £5.00");
        Assert.Equal("THE SHOP", fields.Vendor);
    }

    [Fact]
    public void Extract_VendorTooLong_Skipped()
    {
        // A 70-character line is not a business name; with no other candidate the vendor is null.
        var fields = ReceiptFieldExtractor.Extract(new string('X', 70) + "\nTOTAL £5.00");
        Assert.Null(fields.Vendor);
        Assert.Equal(5.00m, fields.Total);
    }

    [Fact]
    public void Extract_NoTotal_NoCategory()
    {
        var fields = ReceiptFieldExtractor.Extract("THE SHOP\n24 Nov 2026\nThank you");
        Assert.Equal("THE SHOP", fields.Vendor);
        Assert.Equal(new DateTime(2026, 11, 24), fields.Date);
        Assert.Null(fields.Total);
        Assert.Null(fields.SuggestedCategory);
    }

    [Fact]
    public void Extract_FirstDateWins()
    {
        var fields = ReceiptFieldExtractor.Extract("01 Jan 2026\n02 Feb 2026\nTOTAL £5.00");
        Assert.Equal(new DateTime(2026, 1, 1), fields.Date);
        Assert.Null(fields.Vendor); // date lines are never the vendor.
    }

    [Fact]
    public void Extract_NullText_ReturnsAllNull()
    {
        var fields = ReceiptFieldExtractor.Extract(null);
        Assert.Null(fields.Vendor);
        Assert.Null(fields.Date);
        Assert.Null(fields.Total);
        Assert.Null(fields.Currency);
        Assert.Null(fields.SuggestedCategory);
    }
}
