using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Round-FR-4: a public feature request submitted by anyone (authenticated or
/// anonymous). The triage queue at /SystemAdmin/FeatureRequests lets SystemAdmins
/// review, status-change, and add notes to each request.
/// </summary>
public class FeatureRequest
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Required, MaxLength(4000)]
    public string Description { get; set; } = "";

    /// <summary>Submitter display name (may be empty for anonymous).</summary>
    [MaxLength(200)]
    public string? SubmitterName { get; set; }

    /// <summary>Submitter email (optional but helps follow up).</summary>
    [EmailAddress, MaxLength(320)]
    public string? SubmitterEmail { get; set; }

    /// <summary>Identity user id if authenticated; null for anonymous.</summary>
    [MaxLength(128)]
    public string? SubmitterUserId { get; set; }

    /// <summary>IP address for rate-limiting / abuse tracking.</summary>
    [MaxLength(45)]
    public string? SubmitterIp { get; set; }

    public FeatureRequestStatus Status { get; set; } = FeatureRequestStatus.New;

    /// <summary>Internal triage notes visible only to SystemAdmins.</summary>
    [MaxLength(4000)]
    public string? TriageNotes { get; set; }

    /// <summary>Optional link to a PLAN.md anchor (e.g. "Round-FR-4").</summary>
    [MaxLength(200)]
    public string? LinkedSpec { get; set; }

    /// <summary>Honeypot field — must be empty on submission.</summary>
    [MaxLength(200)]
    public string? Honeypot { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When a SystemAdmin last touched the triage row.</summary>
    public DateTime? TriagedUtc { get; set; }

    /// <summary>UserId of the SystemAdmin who last triaged this request.</summary>
    [MaxLength(128)]
    public string? TriagedByUserId { get; set; }
}
