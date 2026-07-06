using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Records that a <see cref="Person"/> completed a specific version of a
/// <see cref="TrainingContent"/> on a given date. Drives validity checks at
/// scheduling time via <c>ExpiresUtc</c>.
/// </summary>
public class TrainingCompletion
{
    public int Id { get; set; }

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int TrainingContentId { get; set; }
    public TrainingContent TrainingContent { get; set; } = null!;

    /// <summary>Snapshot of the content version at the time the completion was recorded.</summary>
    public int TrainingContentVersion { get; set; }

    public DateTime CompletionUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Computed at recording time based on the requirement's cadence.</summary>
    public DateTime? ExpiresUtc { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// Round-FR-2: how this completion was recorded. Round-AV-and-prior
    /// only wrote <see cref="TrainingCompletionSource.UserOnline"/>
    /// (via <c>RecordCompletionAsync</c>'s engagement-eligibility gate).
    /// <see cref="TrainingCompletionSource.CoordinatorManual"/> is the
    /// new in-person-session batch-mark path
    /// (<c>MarkAttendeesCompleteAsync</c>).
    /// <see cref="TrainingCompletionSource.CoordinatorManualSingle"/>
    /// is the new ad-hoc single-volunteer path
    /// (<c>MarkSingleCompleteAsync</c>).
    /// </summary>
    public TrainingCompletionSource CompletionSource { get; set; } = TrainingCompletionSource.UserOnline;

    /// <summary>
    /// Round-FR-2: the coordinator / admin who recorded this manual
    /// mark (plain string — no FK — so the audit trail survives user
    /// deletion, mirroring the <see cref="SystemAdminGrantAudit"/>
    /// pattern). Null for <see cref="TrainingCompletionSource.UserOnline"/>
    /// (the volunteer themselves triggered the mark).
    /// </summary>
    [StringLength(128)]  // matches ASP.NET Identity default Id length
    public string? MarkedCompleteByUserId { get; set; }

    /// <summary>
    /// Round-FR-2: free-form reason for the manual mark (decision Q5:
    /// REQUIRED — marker must type a non-empty reason). Non-empty
    /// validation lives in the marker Razor form + the service layer
    /// (TrainingSessionService / TrainingService). Null for
    /// <see cref="TrainingCompletionSource.UserOnline"/>.
    /// </summary>
    [StringLength(1000)]
    public string? ManualCompletionNotes { get; set; }

    /// <summary>True when <see cref="ExpiresUtc"/> is null or in the future.</summary>
    public bool IsValid(DateTime asOfUtc) => ExpiresUtc is null || ExpiresUtc > asOfUtc;
}
