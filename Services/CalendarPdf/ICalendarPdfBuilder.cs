namespace ServantSync.Services.CalendarPdf;

/// <summary>
/// Library-agnostic interface for generating printable calendar PDFs.
/// The <see cref="QuestPdfCalendarPdfBuilder"/> is the current implementation;
/// swapping to PdfSharpCore or iText7 requires only replacing this single class.
/// </summary>
public interface ICalendarPdfBuilder
{
    /// <summary>
    /// Generates a calendar PDF and writes it to the given stream.
    /// </summary>
    /// <param name="request">The calendar data (slot, org, occurrences, QR URLs).</param>
    /// <param name="qrCodeBuilder">QR code generator for embedding QR codes.</param>
    /// <param name="output">The stream to write the PDF bytes to.</param>
    /// <param name="culture">Culture for formatting (default: current UI culture).</param>
    Task BuildAsync(CalendarPdfRequest request, IQrCodeBuilder qrCodeBuilder, Stream output, string? culture = null);
}
