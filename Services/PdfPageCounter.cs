using UglyToad.PdfPig;

namespace ServantSync.Services;

/// <summary>
/// Reads the page count from an uploaded PDF.
///
/// <para>
/// History: the prior implementation scanned the first 2 MB of the file
/// and counted occurrences of the ASCII token <c>/Type /Page</c> via
/// <see cref="System.Text.RegularExpressions.Regex"/>. That worked for
/// a handful of hand-crafted PDFs whose page dictionaries live in
/// uncompressed "linear objects" near the head of the file, and failed
/// silently for every moderately modern PDF (anything built from
/// Microsoft Word/Excel/PowerPoint, Adobe Acrobat DC, Chrome's print
/// driver, most LaTeX pipelines) because the page dictionaries are
/// routinely stored inside compressed object streams or compressed
/// cross-reference streams — meaning the literal string "/Type /Page"
/// is never present in the raw bytes. The fallback
/// <c>Math.Max(1, n)</c> masked the undercount by reporting "1 page"
/// for any unmatched file, which silently broke the Take page into a
/// frozen "Viewed N of 1 pages" gate and prevented the volunteer from
/// ever marking the training complete.
/// </para>
///
/// <para>
/// This implementation delegates to <see cref="UglyToad.PdfPig"/>, an
/// MIT-licensed mature PDF parser, and reads
/// <see cref="PdfDocument.NumberOfPages"/>. PdfPig walks the document
/// catalog's page tree (decompressing object streams as needed),
/// resolves page dictionaries through compressed-object references,
/// and handles cross-reference-stream PDFs and standard encryption — i.e.
/// every practical case we hit during upload.
/// </para>
/// </summary>
/// <remarks>
/// Contract: callers (the training HandleUpload path in
/// <c>Components/Pages/Training/Edit.razor</c>) wrap this in a try/catch
/// and treat any thrown exception as "page count unknown" — which
/// surfaces to the volunteer as the existing "PDF page count unknown —
/// admin re-upload required" gate. PdfPig throws for encrypted PDFs
/// (PdfDocumentEncryptedException) and for genuinely malformed files;
/// both are caught at the call site and never bubble to the user.
/// </remarks>
public static class PdfPageCounter
{
    public static int Count(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return 0;
        if (!File.Exists(filePath)) return 0;

        using var doc = PdfDocument.Open(filePath);
        return doc.NumberOfPages;
    }
}
