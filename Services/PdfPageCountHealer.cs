using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Self-heal handler: when a volunteer's Take page lands on a PDF whose
/// <see cref="TrainingContent.TotalPageCount"/> is null (legacy upload that
/// predates the PdfPageCounter wiring, or an upload where Edit.razor's
/// extension/ContentType detection silently missed the .pdf case), try
/// to derive the page count from the on-disk file via
/// <see cref="PdfPageCounter"/>. If extraction succeeds, the row is
/// updated (TotalPageCount only — Version is NOT bumped, since an
/// unrelated metadata correction would otherwise invalidate any
/// in-flight TrainingActivity rows keyed to the current content version).
///
/// <para>
/// Round-AI follow-on to round-AH: the "PDF page count unknown —
/// admin re-upload required" gate at TrainingService.cs#CheckEligibilityAsync
/// is correct for the trust boundary, but only if TotalPageCount was
/// actually missing. For legacy content where the original upload
/// predates the PdfPageCounter wiring — or for files whose extension
/// check at Edit.razor#HandleUpload missed (encrypted PDF, missing
/// extension, try/catch swallowing PdfPig exceptions) — the volunteer
/// was stuck until an admin re-uploaded. This healer closes the
/// metadata gap automatically on the volunteer's next visit.
/// </para>
///
/// <para>
/// Trust-boundary note: a volunteer's read-path request mutates
/// <see cref="TrainingContent.TotalPageCount"/>. This is bounded — the
/// operation only writes a non-negative integer derived from the
/// canonical byte-for-byte content of the PDF, is idempotent (a second
/// call returns the same integer), cannot introduce a downgrade
/// (heal only writes when PdfPig returns &gt; 0), and cannot bypass
/// the existing org-membership gate (the caller is responsible for
/// gating the call to members of the content's owning organization).
/// </para>
/// </summary>
public interface IPdfPageCountHealer
{
    /// <summary>
    /// Returns the (possibly newly-populated) <see cref="TrainingContent.TotalPageCount"/>
    /// for <paramref name="trainingContentId"/>, or null if the row was
    /// unchanged / extraction failed.
    /// </summary>
    Task<int?> TryHealAsync(int trainingContentId, CancellationToken ct = default);
}

public class PdfPageCountHealer : IPdfPageCountHealer
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IUploadPathProvider _uploads;

    public PdfPageCountHealer(IDbContextFactory<ApplicationDbContext> factory, IUploadPathProvider uploads)
    {
        _factory = factory;
        _uploads = uploads;
    }

    public async Task<int?> TryHealAsync(int trainingContentId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // AsNoTracking on the read pass — we only use this row to decide
        // whether a Write is needed; the tracked FindAsync below is what
        // actually persists the change.
        var content = await db.TrainingContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == trainingContentId, ct);
        if (content is null) return null;
        if (content.TotalPageCount.HasValue) return content.TotalPageCount;
        if (content.Format != TrainingFormat.Pdf) return null;
        if (string.IsNullOrWhiteSpace(content.FilePathOrUrl)) return null;
        if (content.FilePathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

        // Map the URL-style relative path back to the on-disk absolute
        // path via UploadPathProvider. GetTrainingUploadPath strips any
        // path-traversal attempts (Path.GetFileName on the leaf), so a
        // maliciously-crafted URL like /uploads/training/../etc/passwd
        // is bounded to the file's leaf name only.
        var leaf = Path.GetFileName(content.FilePathOrUrl);
        if (string.IsNullOrEmpty(leaf)) return null;
        var path = _uploads.GetTrainingUploadPath(leaf);
        if (!File.Exists(path)) return null;

        int pages;
        try { pages = PdfPageCounter.Count(path); }
        catch { return null; } // encrypted / malformed — keep gate as-is
        if (pages <= 0) return null;

        var row = await db.TrainingContents.FindAsync(trainingContentId, ct);
        if (row is null) return null;
        if (row.TotalPageCount == pages) return null;
        row.TotalPageCount = pages;
        // Deliberately do NOT bump Version here — the activity contract
        // in Services/TrainingService.cs#SyncActivityAsync keys on
        // (UserId, ContentId, ContentVersion), and a metadata-only
        // correction does not warrant invalidating any in-flight
        // activity the volunteer may have already accumulated.
        await db.SaveChangesAsync(ct);
        return pages;
    }
}
