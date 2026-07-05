using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A sponsoring organization (church, club, league). The root of the
/// ministry/role hierarchy. A person can participate in many organizations.
/// </summary>
public class Organization
{
    public int Id { get; set; }

    [Required, StringLength(160)]
    public string Name { get; set; } = null!;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(120), EmailAddress]
    public string? ContactEmail { get; set; }

    [StringLength(40)]
    public string? ContactPhone { get; set; }

    /// <summary>
    /// Optional per-org IANA timezone id (e.g. "America/Chicago"). When set,
    /// the organization has implicitly declared "all of my coordinators and
    /// my service-slot times should be interpreted in this zone". Round-AV
    /// uses this as the fallback for <c>LocalTime</c> rendering — after
    /// <c>UserTimeZoneProvider</c> (browser-detected) fails to resolve the
    /// user's clock, we shift to org-tz, then finally to server local.
    /// Canonical consumer: <c>Components.Shared.LocalTime</c>'s
    /// <c>FallbackTimeZoneId</c> parameter.
    ///
    /// Seeded <c>NULL</c>: legacy orgs continue to defer to the prerender
    /// server-local fallback (the prior round-N behavior); admins set this
    /// via the timezone picker on <c>Components/Pages/Organizations/Edit.razor</c>.
    ///
    /// Width: <c>StringLength(64)</c> is generous — the longest IANA zone in
    /// the public tz database is currently <c>"America/Argentina/ComodRivadavia"</c>
    /// at 41 chars, so 64 is a safe upper bound that future timezone
    /// additions won't outgrow.
    /// </summary>
    [StringLength(64)]
    public string? TimeZoneId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Per-org registration token. Admins share the matching
    /// <c>/Account/Register?token=&lt;RegistrationToken&gt;</c> URL with
    /// prospective volunteers. Visiting that URL auto-joins the new account
    /// to this org as <see cref="OrganizationRole.Volunteer"/>. Null until
    /// generated — either on Create (via <c>OrganizationService.CreateOrgAsync</c>)
    /// or by an Admin invoking the rotation handler. Indexed unique in
    /// <c>ApplicationDbContext</c> so tokens can't collide across orgs;
    /// SQLite allows multiple NULLs so existing rows are unaffected.
    /// </summary>
    [StringLength(32)]
    public string? RegistrationToken { get; set; }

    public ICollection<Ministry> Ministries { get; set; } = new List<Ministry>();

    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();

    /// <summary>Training content catalog owned by this organization.</summary>
    public ICollection<TrainingContent> TrainingContents { get; set; } = new List<TrainingContent>();

    /// <summary>Org-wide training that every volunteer in this org must keep current.</summary>
    public ICollection<TrainingRequirement> TrainingRequirements { get; set; } = new List<TrainingRequirement>();
}
