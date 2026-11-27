using DocuArchive.Application;

namespace DocuArchive.Tests;

/// <summary>
/// Magic-byte file-type detection: the client-supplied content type and
/// extension are never trusted, so every allowed type must be recognised by
/// its signature alone, and anything else (including truncated signatures)
/// must return null.
/// </summary>
public class FileValidationTests
{
    [Fact]
    public void DetectMimeType_PdfHeader_ReturnsPdf()
    {
        Assert.Equal("application/pdf", FileValidation.DetectMimeType("%PDF-1.4\n"u8.ToArray()));
    }

    [Fact]
    public void DetectMimeType_PngSignature_ReturnsPng()
    {
        Assert.Equal("image/png", FileValidation.DetectMimeType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
    }

    [Fact]
    public void DetectMimeType_JpegSignature_ReturnsJpeg()
    {
        Assert.Equal("image/jpeg", FileValidation.DetectMimeType([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]));
    }

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void DetectMimeType_GifVersions_ReturnGif(string header)
    {
        Assert.Equal("image/gif", FileValidation.DetectMimeType(System.Text.Encoding.ASCII.GetBytes(header)));
    }

    [Fact]
    public void DetectMimeType_TiffLittleEndian_ReturnsTiff()
    {
        Assert.Equal("image/tiff", FileValidation.DetectMimeType([0x49, 0x49, 0x2A, 0x00]));
    }

    [Fact]
    public void DetectMimeType_TiffBigEndian_ReturnsTiff()
    {
        Assert.Equal("image/tiff", FileValidation.DetectMimeType([0x4D, 0x4D, 0x00, 0x2A]));
    }

    [Fact]
    public void DetectMimeType_BmpSignature_ReturnsBmp()
    {
        Assert.Equal("image/bmp", FileValidation.DetectMimeType([0x42, 0x4D, 0x36, 0x00]));
    }

    [Fact]
    public void DetectMimeType_WebP_RiffWebp_ReturnsWebP()
    {
        // RIFF + 4 size bytes + WEBP.
        Assert.Equal("image/webp", FileValidation.DetectMimeType(
            [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]));
    }

    [Fact]
    public void DetectMimeType_PlainText_ReturnsNull()
    {
        Assert.Null(FileValidation.DetectMimeType(System.Text.Encoding.UTF8.GetBytes("This is plain text.")));
    }

    [Fact]
    public void DetectMimeType_EmptyInput_ReturnsNull()
    {
        Assert.Null(FileValidation.DetectMimeType([]));
    }

    [Fact]
    public void DetectMimeType_TruncatedPdf_ReturnsNull()
    {
        // Only 3 bytes of "%PDF" — the 4-byte check must not pass.
        Assert.Null(FileValidation.DetectMimeType([0x25, 0x50, 0x44]));
    }

    [Fact]
    public void DetectMimeType_TruncatedPng_ReturnsNull()
    {
        // Only the first 4 PNG bytes — the 8-byte check must not pass.
        Assert.Null(FileValidation.DetectMimeType([0x89, 0x50, 0x4E, 0x47]));
    }

    [Fact]
    public void DetectMimeType_GifWithBadVersion_ReturnsNull()
    {
        // GIF86a is not an allowed version.
        Assert.Null(FileValidation.DetectMimeType(System.Text.Encoding.ASCII.GetBytes("GIF86a")));
    }
}
