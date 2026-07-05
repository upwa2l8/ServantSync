using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Per-user progress on a single version of a <see cref="TrainingContent"/>.
/// Drive all "did the volunteer actually engage with this before marking
/// complete" rules from this row server-side so a hostile client can't
/// forge an eligible state.
/// Row is reset by <see cref="TrainingContentVersion"/> — when the admin
/// bumps a content's version (Edit.razor always increments on Save),
/// the previous activity no longer qualifies for the new version, so
/// the volunteer re-engages before re-completing.
/// </summary>
public class TrainingActivity
{
    public int Id { get; set; }

    [Required]
    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int TrainingContentId { get; set; }
    public TrainingContent TrainingContent { get; set; } = null!;

    /// <summary>
    /// Snapshot of the content version this activity was recorded against;
    /// if the admin bumps the version, the volunteer needs a fresh
    /// activity row to claim completion of the new version.
    /// </summary>
    public int TrainingContentVersion { get; set; }

    /// <summary>
    /// Comma-separated 1-based page indices the volunteer has actually
    /// viewed (PDF only). Empty for non-PDF formats. Stored as a flat
    /// TEXT column for fast append + parse — the union-with-newest
    /// coalesce in SyncActivityAsync doesn't need JSON functions.
    /// </summary>
    [StringLength(4000)]
    public string ViewedPagesCsv { get; set; } = "";

    /// <summary>
    /// Highest second-reached of a video (uploaded or YT/Vimeo embed).
    /// Monotonic — sync coalesces with <c>Math.Max</c> so an attacker
    /// can't burn down by sending smaller values.
    /// </summary>
    public int HighestWatchedSec { get; set; }

    /// <summary>
    /// The actual duration reported by the media (PDF page count for PDF,
    /// <c>video.duration</c> for HTML5 video, YT <c>getDuration()</c> for
    /// embeds). Used as the denominator for the 95% / every-page
    /// eligibility rule. Default 0 → server treats the activity as
    /// ineligible until the client reports a real value.
    /// </summary>
    public int ActualDurationSec { get; set; }

    /// <summary>
    /// Captured on first sync. Used as the "you've been on this training
    /// for at least X seconds" timer for best-effort formats
    /// (Slideshow / External URL) and as a fallback when
    /// <see cref="ActualDurationSec"/> hasn't been reported yet.
    /// </summary>
    public DateTime FirstOpenedUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Parses <see cref="ViewedPagesCsv"/> into a HashSet of int page
    /// numbers. Caller-mutable; serialized back on Save by the service.
    /// </summary>
    public HashSet<int> GetViewedPages()
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(ViewedPagesCsv)) return set;
        foreach (var part in ViewedPagesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var p) && p > 0) set.Add(p);
        }
        return set;
    }

    public static string SerializeViewedPages(IEnumerable<int> pages) =>
        string.Join(",", pages.OrderBy(p => p));
}
