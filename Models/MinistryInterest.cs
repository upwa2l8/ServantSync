using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Junction table linking a <see cref="Person"/> to a <see cref="Ministry"/>
/// they have expressed interest in volunteering for. Distinct from
/// <see cref="OrganizationMembership"/>: that row carries RBAC role and
/// grants management power, while this row is a soft preference signal
/// that filters the user's open-shift browsing surface.
/// <para>
/// Composite-unique on (<see cref="PersonUserId"/>, <see cref="MinistryId"/>)
/// ensures a person cannot mark the same ministry as "interested" twice.
/// </para>
/// </summary>
public class MinistryInterest
{
    public int Id { get; set; }

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int MinistryId { get; set; }
    public Ministry Ministry { get; set; } = null!;

    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? Notes { get; set; }
}
