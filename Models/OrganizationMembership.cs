using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Junction table linking a <see cref="Person"/> to an <see cref="Organization"/>
/// with a specific role. Composite-unique on (PersonUserId, OrganizationId)
/// means a person cannot have duplicate memberships in the same org.
/// </summary>
public class OrganizationMembership
{
    public int Id { get; set; }

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public OrganizationRole Role { get; set; } = OrganizationRole.Volunteer;

    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? Notes { get; set; }
}
