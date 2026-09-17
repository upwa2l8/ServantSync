using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of creating a trade request.</summary>
public enum SlotTradeRequestResult
{
    /// <summary>Persisted as Pending; the serving volunteer (+ coordinators) were notified.</summary>
    Succeeded = 0,

    /// <summary>Caller isn't allowed to file requests for that target (not a member of the owning org, or not an admin when filing on behalf of someone else).</summary>
    PermissionDenied = 1,

    /// <summary>Target assignment doesn't exist.</summary>
    AssignmentNotFound = 2,

    /// <summary>Target is no longer Scheduled, already belongs to the requester, sits in the past, or the target slot is inactive.</summary>
    TargetNotTradeable = 3,

    /// <summary>Caller already has a Pending request for this exact shift.</summary>
    DuplicatePendingRequest = 4,

    /// <summary>Requester is missing required training for the target slot — coordinator must resolve before a trade can be filed.</summary>
    TrainingNotCompliant = 5,
}

/// <summary>Outcome of approve / decline / cancel.</summary>
public enum SlotTradeDecisionResult
{
    /// <summary>Approved: Assignment re-parented to the requester + this row marked Approved. (For decline/cancel: row marked, nothing else changed.)</summary>
    Succeeded = 0,

    /// <summary>Caller isn't the serving volunteer nor coordinator-and-above (approve/decline), or isn't the requester (cancel).</summary>
    PermissionDenied = 1,

    /// <summary>Request row doesn't exist.</summary>
    NotFound = 2,

    /// <summary>The request is no longer Pending (already decided), or on approval the shift is no longer tradeable (assignment cancelled, shift started, or the serving volunteer changed via a different approved trade).</summary>
    StaleOrAlreadyDecided = 3,

    /// <summary>Approval failed late validation (conflict, missing training, or capacity) — reasons are surfaced to the caller via <see cref="SlotTradeDecision.Conflicts"/>. The request stays Pending.</summary>
    ValidationFailed = 4,
}

/// <summary>Rich result carrying per-failure reasons (mirrors <see cref="AssignmentValidationResult"/> semantics).</summary>
public record SlotTradeDecision(
    SlotTradeDecisionResult Result,
    Assignment? Assignment,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> MissingTrainings)
{
    public static SlotTradeDecision Ok(Assignment a) =>
        new(SlotTradeDecisionResult.Succeeded, a, Array.Empty<string>(), Array.Empty<string>());

    public static SlotTradeDecision Fail(
        SlotTradeDecisionResult result,
        IReadOnlyList<string>? conflicts = null,
        IReadOnlyList<string>? missingTrainings = null) =>
        new(result, null, conflicts ?? Array.Empty<string>(), missingTrainings ?? Array.Empty<string>());
}

/// <summary>
/// One row of the /Trades queue view. Combines the request with the
/// denormalized names + shift description the UI renders, so the page
/// never has to stitch entities together.
/// </summary>
public record SlotTradeRequestView(
    int RequestId,
    SlotTradeRequestStatus Status,
    string RequesterUserId,
    string RequesterName,
    string TargetOwnerName,
    int TargetAssignmentId,
    int ServiceSlotId,
    string SlotName,
    string MinistryName,
    int OrganizationId,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Location,
    /// <summary>True when the viewer is one of the listed approvers for this row.</summary>
    bool ViewerCanApprove,
    /// <summary>True when the viewer is the requester (cancel button).</summary>
    bool ViewerIsRequester,
    string? Message,
    DateTime CreatedUtc);

/// <summary>
/// Slot trade flow (Round-TRADE). A volunteer requests a filled shift;
/// EITHER the serving volunteer OR a coordinator-and-above
/// (MinistryDirector / OrgAdmin / SystemAdmin of the owning org) approves.
/// On approval the Assignment is re-parented to the requester inside one
/// SaveChanges — the request row and the schedule can never disagree.
/// </summary>
public interface ISlotTradeService
{
    /// <summary>
    /// Files a Pending trade request on <paramref name="targetAssignmentId"/>.
    /// The requester must be a member of the owning org (or a SystemAdmin),
    /// the target must be a future Scheduled Assignment on an active slot
    /// that isn't already theirs, they must have no Pending request for the
    /// same shift, and they must be training-compliant for the slot.
    /// onBehalfOfUserId != userId requires org-admin authority (admin files
    /// for a phone-call volunteer).
    /// </summary>
    Task<SlotTradeRequestResult> RequestTradeAsync(
        string userId,
        int targetAssignmentId,
        string? onBehalfOfUserId = null,
        string? message = null,
        CancellationToken ct = default);

    /// <summary>
    /// Approve the Pending request as the serving volunteer or a
    /// coordinator-and-above. Re-validates everything at approval time
    /// (assignment still Scheduled, still future, owner unchanged, requester
    /// still conflict-free + trained) and only then re-parents the
    /// Assignment to the requester. First-approver-wins: a second concurrent
    /// approve gets <see cref="SlotTradeDecisionResult.StaleOrAlreadyDecided"/>.
    /// </summary>
    Task<SlotTradeDecision> ApproveAsync(
        int requestId,
        string approverUserId,
        CancellationToken ct = default);

    /// <summary>Decline the Pending request (serving volunteer or coordinator-and-above). Terminal; nothing else changes.</summary>
    Task<SlotTradeDecision> DeclineAsync(
        int requestId,
        string approverUserId,
        CancellationToken ct = default);

    /// <summary>The requester withdraws their own Pending request. Terminal.</summary>
    Task<SlotTradeDecision> CancelAsync(
        int requestId,
        string requesterUserId,
        CancellationToken ct = default);

    /// <summary>Queue view: Pending requests where the viewer is a listed approver (serving them or coordinator-and-above of the owning org).</summary>
    Task<List<SlotTradeRequestView>> ListPendingForApproverAsync(string viewerUserId, CancellationToken ct = default);

    /// <summary>Queue view: Pending requests targeting shifts the viewer currently serves (covers viewer-is-servant even when not an org member).</summary>
    Task<List<SlotTradeRequestView>> ListPendingForServingVolunteerAsync(string viewerUserId, CancellationToken ct = default);

    /// <summary>Queue view: the viewer's own requests (any status), newest first.</summary>
    Task<List<SlotTradeRequestView>> ListMineAsync(string viewerUserId, CancellationToken ct = default);
}
