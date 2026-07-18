using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using QRCoder;

namespace ServantSync.Services.CalendarPdf;

/// <summary>
/// Thin wrapper over <see cref="QRCoder"/> for generating QR code PNG byte arrays.
/// Provides a test seam so the PDF builder can be tested without real QR generation.
/// Error correction level M (15% recovery) — good balance for print degradation
/// and phone cameras at 1–2m. Module size 4px, quiet zone 4 modules.
/// </summary>
public interface IQrCodeBuilder
{
    /// <summary>
    /// Generates a QR code PNG byte array for the given URL.
    /// </summary>
    /// <param name="url">The URL to encode.</param>
    /// <param name="pixelSize">The pixel size of each QR module (default 4).</param>
    /// <returns>PNG byte array, or null if encoding fails.</returns>
    byte[]? GeneratePng(string url, int pixelSize = 4);
}

public class QrCodeBuilder : IQrCodeBuilder
{
    public byte[]? GeneratePng(string url, int pixelSize = 4)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);

            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(pixelSize);
        }
        catch
        {
            // QR encode failure (extremely long URL overflows QR capacity).
            // Caller falls back to printed-text variant + logs a warning.
            return null;
        }
    }
}
