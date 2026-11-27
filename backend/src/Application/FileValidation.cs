namespace DocuArchive.Application;

/// <summary>Upload/storage settings, bound from the <c>Storage</c> configuration section.</summary>
public class StorageSettings
{
    /// <summary>Maximum upload size in bytes. Default 25 MiB.</summary>
    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    /// <summary>Local storage root (used by the Infrastructure filesystem implementation).</summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>File-type validation by magic bytes (Phase 2). The client-supplied content type / extension is never trusted.</summary>
public static class FileValidation
{
    /// <summary>Maximum bytes inspected for the magic-byte signature.</summary>
    public const int SignatureLength = 16;

    /// <summary>Detects the real MIME type from the file's magic bytes, or null when the type is not allowed.</summary>
    public static string? DetectMimeType(byte[] header)
    {
        if (header.Length >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            return "application/pdf"; // %PDF
        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return "image/png";
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";
        if (header.Length >= 6 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38
            && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
            return "image/gif"; // GIF87a / GIF89a
        if (header.Length >= 4 && header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
            return "image/tiff"; // II*\0 (little-endian TIFF)
        if (header.Length >= 4 && header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
            return "image/tiff"; // MM\0* (big-endian TIFF)
        if (header.Length >= 2 && header[0] == 0x42 && header[1] == 0x4D)
            return "image/bmp"; // BM
        if (header.Length >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return "image/webp"; // RIFF....WEBP
        return null;
    }

    /// <summary>
    /// True when a filename extension (without the dot, lower-cased) is one of the normal
    /// extensions for the detected MIME type. Files without an extension are accepted by the
    /// caller; a present extension that disagrees with the magic bytes is rejected there.
    /// </summary>
    public static bool ExtensionMatchesMime(string extension, string mimeType) => (extension, mimeType) switch
    {
        ("pdf", "application/pdf") => true,
        ("png", "image/png") => true,
        ("jpg" or "jpeg", "image/jpeg") => true,
        ("gif", "image/gif") => true,
        ("tif" or "tiff", "image/tiff") => true,
        ("bmp", "image/bmp") => true,
        ("webp", "image/webp") => true,
        _ => false
    };
}
