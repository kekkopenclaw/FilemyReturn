namespace DocuArchive.Application;

/// <summary>
/// Renders a QR code as an SVG string (Phase 7 physical archive). The payload
/// is a deep link to a physical box (<c>/boxes?box={Code}</c>); the SVG is
/// served by the boxes controller for printing on the box label. SVG (not a
/// bitmap) so no System.Drawing / native graphics library is needed at
/// runtime — the label prints crisply at any size.
/// </summary>
public interface IQrCodeService
{
    /// <summary>Renders <paramref name="payload"/> as a standalone SVG document.</summary>
    string RenderSvg(string payload);
}
