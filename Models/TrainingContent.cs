using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A piece of training material (video, slideshow, or PDF), owned by exactly
/// one <see cref="Organization"/>. Versioned so <see cref="TrainingCompletion"/> rows
/// record which version was completed and we can invalidate stale completions
/// when content changes.
/// </summary>
public class TrainingContent
{
    public int Id { get; set; }

    /// <summary>
    /// Owning organization. Strict 1:N — a TrainingContent is created by an
    /// admin of (and belongs to) a single org, and never re-used by other orgs.
    /// Mirrored FK in EF; cascade-delete with the parent org (matches
    /// <see cref="Ministry"/>'s model). The unique-within-org semantics
    /// ("Safe Spaces 101" can exist independently in two orgs) is intentional —
    /// cross-org re-use was a privacy/UX hazard we explicitly removed.
    /// </summary>
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required, StringLength(200)]
    public string Title { get; set; } = null!;

    [StringLength(2000)]
    public string? Description { get; set; }

    public TrainingFormat Format { get; set; }

    /// <summary>
    /// For <see cref="TrainingFormat.Video"/>: an embed URL (YouTube/Vimeo).
    /// For PDF / slideshow: a relative path under <c>wwwroot/uploads/training/</c>,
    /// or an absolute URL.
    /// Null for <see cref="TrainingFormat.InPerson"/> (no online file).
    /// </summary>
    [StringLength(600)]
    public string? FilePathOrUrl { get; set; }

    /// <summary>Optional metadata shown alongside the training.</summary>
    public TimeSpan? EstimatedDuration { get; set; }

    /// <summary>
    /// Total page count for an uploaded PDF, populated server-side on
    /// upload (see <c>Services/PdfPageCounter.cs</c>). The eligibility
    /// rule reads this column to verify "viewed every page". Null
    /// when the content isn't a PDF (or the page count hasn't been
    /// computed yet — the rule falls back to the duration heuristic).
    /// </summary>
    public int? TotalPageCount { get; set; }

    public int Version { get; set; } = 1;

    public DateTime VersionDateUtc { get; set; } = DateTime.UtcNow;

    [StringLength(80)]
    public string? VersionLabel { get; set; }
}
