using DocuArchive.Application;
using QRCoder;

namespace DocuArchive.Infrastructure.Qr;

/// <summary>
/// QR codes for physical-box labels (Phase 7). QRCoder (MIT, free, no native
/// dependencies) renders the payload as SVG — no System.Drawing / libgdiplus
/// needed at runtime, so the label prints crisply at any size. Only the SVG
/// path is used; the bitmap paths are never called. Error correction level Q
/// (25%) so a scuffed or dusty label still scans.
/// </summary>
public class QrCodeService : IQrCodeService
{
    public string RenderSvg(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("QR payload is required.", nameof(payload));
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(pixelsPerModule: 8);
    }
}
