using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// In-person training event: scheduled date/time/location, optionally
/// tied to a <see cref="TrainingContent"/> (the material covered).
/// Round-FR-2 implementation.
///
/// RBAC: coordinators / admins of the org can create / edit / cancel;
/// volunteers (org members) can sign up / cancel their own sign-up
/// via <see cref="TrainingSessionAttendee"/>.
///
/// Status lifecycle:
/// <see cref="TrainingSessionStatus.Scheduled"/> on creation,
/// → <see cref="TrainingSessionStatus.Cancelled"/> if cancelled pre-event,
/// → <see cref="TrainingSessionStatus.Completed"/> after the marker
///   records attendance via
///   <c>ITrainingSessionService.MarkAttendeesCompleteAsync</c>.
///
/// <see cref="CreatedByUserId"/> is a plain string column (no FK)
/// so the session row survives user deletion — the audit trail
/// outlives the coordinator who authored it, mirroring the
/// <see cref="SystemAdminGrantAudit"/> pattern.
/// </summary>
public class TrainingSession
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Optional — null means "general orientation" with no specific
    /// digital content covered. FK uses <see cref="DeleteBehavior.SetNull"/>
    /// so deleting the content preserves session history.
    /// </summary>
    public int? TrainingContentId { get; set; }
    public TrainingContent? TrainingContent { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = null!;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(200)]
    public string Location { get; set; } = null!;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>
    /// Optional capacity. Sign-ups past this are refused with a
    /// "session at capacity" message (decision Q1: enforce).
    /// Null = no enforced capacity.
    /// </summary>
    public int? MaxAttendees { get; set; }

    public TrainingSessionStatus Status { get; set; } = TrainingSessionStatus.Scheduled;

    /// <summary>
    /// Plain string column (no FK) so the session row survives
    /// user deletion — matches <see cref="SystemAdminGrantAudit"/>'s
    /// "audit trail outlives the actor" pattern.
    /// </summary>
    public string CreatedByUserId { get; set; } = null!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<TrainingSessionAttendee> Attendees { get; set; } = new List<TrainingSessionAttendee>();
}
