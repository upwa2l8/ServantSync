using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Round-FR-6: counts widget for the "Training due soon (N at-risk)"
/// badge in <c>Organizations/Detail.razor</c>'s Org-training tab
/// (mirrors the "In-person training sessions (N upcoming)" badge shape
/// from Round-FR-2.3). The grid page uses the full <see cref="ListAtRiskAsync"/>
/// projection; the badge uses this lighter aggregate so the org-detail
/// Reload() doesn't pull every row just to count them.
/// </summary>
public sealed class TrainingDueSoonCounts
{
    public int OverdueCount { get; set; }
    public int DueSoonCount { get; set; }
    /// <summary>Sum of the two — what the "(N at-risk)" badge displays.</summary>
    public int TotalAtRiskCount => OverdueCount + DueSoonCount;
}

/// <summary>
/// Round-FR-6: one row in the training-due-soon grid. PersonKeyed by
/// <c>PersonUserId</c> (the project's PK pattern for all Person FKs),
/// not <c>Person.Id</c> (which doesn't exist as a column — the schema
/// uses <c>UserId</c> as both the PK and the FK to AspNetUsers.Id, per
/// the existing Person model).
/// </summary>
public sealed class TrainingDueSoonRow
{
    public string PersonUserId { get; set; } = "";
    public string PersonDisplayName { get; set; } = "";
    /// <summary>Round-FR-6 decision Q4: stub People ARE surfaced alongside real volunteers.</summary>
    public bool IsStub { get; set; }
    public string? EmailAtMoment { get; set; }
    public int RequirementId { get; set; }
    public int TrainingContentId { get; set; }
    public string RequirementTitle { get; set; } = "";
    /// <summary>"Org" or "Slot · {SlotName}". Drives the muted small-text label in the grid table.</summary>
    public string RequirementScope { get; set; } = "";
    public int? SlotId { get; set; }
    public string? SlotName { get; set; }
    public DateTime? LastCompletionUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public TrainingCompletionSource? CompletionSource { get; set; }
    public TrainingDueSoonStatus Status { get; set; }
    /// <summary>
    /// Positive = days until expiry. Negative = days overdue. Null when
    /// status is Compliant or NotRequired (the grid hides the column
    /// for those rows; the value is irrelevant).
    /// </summary>
    public int? DaysDelta { get; set; }
}

/// <summary>
/// Round-FR-6: per-org "training due soon" grid (see <c>PLAN.md</c> →
/// Feature requests → Round-FR-6). Service is the security boundary —
/// Admin or MinistryDirector gate (Slot Coordinators and Volunteers
/// are denied per the FR-6 RBAC matrix). Math drives off the existing
/// <c>TrainingCompletion.ExpiresUtc</c> field (NO schema migration).
/// Stub People (Round-FR-3) are included alongside real volunteers per
/// spec decision Q4.
/// </summary>
public interface ITrainingDueSoonService
{
    /// <summary>
    /// Counts widget for the org-detail tab badge. Same RBAC + same
    /// cross-product query as <see cref="ListAtRiskAsync"/> but projected
    /// to two counters (OverdueCount, DueSoonCount) so the org-detail
    /// Reload() doesn't materialize every row just to count them.
    /// </summary>
    Task<TrainingDueSoonCounts> ListAtRiskCountsAsync(
        int organizationId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Full at-risk projection for the DueSoon.razor grid. Filter chip
    /// (AllAtRisk / OverdueOnly / DueIn30Days / CompletedRecently) +
    /// sort toggle (ByUrgency / ByPersonName / ByContentTitle) select
    /// which subset of the cross-product to surface. Returns
    /// <see cref="UnauthorizedAccessException"/> if caller isn't Admin
    /// or MinistryDirector in <paramref name="organizationId"/> (the
    /// exception type lets pages branch without a result-enum indirection;
    /// tests assert against <c>Assert.ThrowsAsync&lt;UnauthorizedAccessException&gt;</c>).
    /// </summary>
    Task<List<TrainingDueSoonRow>> ListAtRiskAsync(
        int organizationId,
        TrainingDueSoonFilter filter,
        TrainingDueSoonSort sort,
        string callerUserId,
        CancellationToken ct = default);
}
