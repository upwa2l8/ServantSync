using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Lifecycle of a slot-trade request. Created by a volunteer who wants a
/// filled shift; ends in exactly one terminal state (Approved, Declined,
/// or Cancelled) or stays Pending.
/// </summary>
public enum SlotTradeRequestStatus
{
    /// <summary>Waiting on the serving volunteer and/or a coordinator.</summary>
    Pending = 0,

    /// <summary>Terminal. The Assignment was re-parented to the requester.</summary>
    Approved = 1,

    /// <summary>Terminal. An approver refused; nothing changed.</summary>
    Declined = 2,

    /// <summary>Terminal. The requester withdrew their own request.</summary>
    Cancelled = 3,
}

/// <summary>Who performed an approval, recorded on the row for auditability.</summary>
public enum SlotTradeRequestApproverRole
{
    /// <summary>Approval came from the volunteer currently serving the shift.</summary>
    ServingVolunteer = 0,

    /// <summary>Approval came from a MinistryDirector of the owning ministry.</summary>
    MinistryDirector = 1,

    /// <summary>Approval came from an org admin of the owning org.</summary>
    OrgAdmin = 2,

    /// <summary>Approval came from a system admin (outside the org entirely).</summary>
    SystemAdmin = 3,
}

/// <summary>
/// A volunteer's request to take over another volunteer's filled shift
/// (an <see cref="Assignment"/> with status Scheduled at a specific
/// (ServiceSlotId, StartUtc) pair). Created from the /Open page when the
/// "Show filled shifts" toggle reveals who is serving.
///
/// Lifecycle (see <see cref="SlotTradeRequestStatus"/>): a Pending request
/// can be approved by EITHER the serving volunteer OR a coordinator-and-above
/// (MinistryDirector / OrgAdmin / SystemAdmin). On approval the service
/// re-parents the target Assignment to the requester inside one
/// SaveChanges transaction and marks this row Approved — there is no
/// intermediate "approved but not swapped" state, so the schedule and the
/// trade row can never disagree.
///
/// Referential policy: RequesterUserId cascades on Person delete (a dead
/// volunteer's requests are noise). TargetAssignmentId cascades on
/// Assignment delete — an approved trade's swap already happened, a pending
/// trade's target no longer exists and can never be approved, so cascade
/// (rather than SetNull) is correct and self-documenting. TargetOwnerUserId
/// is a plain string with no FK nav: it is denormalized audit data ("who
/// was serving when this was requested") and must survive that person's
/// account being removed. RequestedForUserId is a plain string column with
/// no FK — set by an admin on behalf of a volunteer who can't use the site.
/// </summary>
public class SlotTradeRequest
{
    public int Id { get; set; }

    /// <summary>The volunteer asking to take the shift.</summary>
    public string RequesterUserId { get; set; } = null!;
    public Person Requester { get; set; } = null!;

    /// <summary>The filled shift being requested (unique Assignment row).</summary>
    public int TargetAssignmentId { get; set; }
    public Assignment TargetAssignment { get; set; } = null!;

    /// <summary>Denormalized for list rendering + audit: who currently holds the shift.</summary>
    public string TargetOwnerUserId { get; set; } = null!;

    /// <summary>Optional proxy: an admin can file a request for a volunteer
    /// who can't use the site (matches TrainingSessionService.SignUpAsync's
    /// userId, onBehalfOfUserId pattern). Equals RequesterUserId when self-filed.</summary>
    public string RequestedForUserId { get; set; } = null!;

    public SlotTradeRequestStatus Status { get; set; } = SlotTradeRequestStatus.Pending;

    /// <summary>Set on approve/decline/cancel. Null while Pending.</summary>
    public DateTime? DecidedUtc { get; set; }

    /// <summary>User id of the decider (serving volunteer or coordinator). Null while Pending.</summary>
    public string? DecidedByUserId { get; set; }

    /// <summary>Which authority granted an approval. Null unless Status == Approved.</summary>
    public SlotTradeRequestApproverRole? ApproverRole { get; set; }

    [StringLength(500)]
    public string? Message { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
