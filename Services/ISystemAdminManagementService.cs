namespace ServantSync.Services;

/// <summary>
/// Outcome of an attempt to grant the SystemAdmin role to a target user.
/// Distinct result values let the page surface a specific error message
/// (e.g. "this user already has SystemAdmin") instead of a generic denial.
/// </summary>
public enum SystemAdminGrantResult
{
    /// <summary>The SystemAdmin role was successfully attached to the target user.</summary>
    Success,

    /// <summary>The caller is not themself a SystemAdmin. The grant is refused; an audit row is written.</summary>
    PermissionDenied,

    /// <summary>The target userId does not resolve to an IdentityUser row.</summary>
    TargetUserNotFound,

    /// <summary>The target user is already a SystemAdmin. The grant is refused (no-op); an audit row is written.</summary>
    AlreadyHasRole,
}

/// <summary>
/// Outcome of an attempt to revoke the SystemAdmin role from a target user.
/// </summary>
public enum SystemAdminRevokeResult
{
    /// <summary>The SystemAdmin role was successfully removed from the target user.</summary>
    Success,

    /// <summary>The caller is not themself a SystemAdmin. Refused; audit row written.</summary>
    PermissionDenied,

    /// <summary>The target userId does not resolve to an IdentityUser row.</summary>
    TargetUserNotFound,

    /// <summary>The target user did not have the SystemAdmin role to begin with. Refused; audit row written.</summary>
    DoesNotHaveRole,

    /// <summary>The actor tried to revoke the role from themself. Refused (no last-SysAdmin lockout); audit row written.</summary>
    SelfRevokeRefused,
}

/// <summary>
/// Row DTO for the SystemAdmin roster — what's bound to the table on
/// <c>/SystemAdmin</c>. Includes the IdentityUser's email + Person's
/// display name where available so SysAdmins see friendly names instead
/// of GUIDs.
/// </summary>
public class SystemAdminRow
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime GrantedUtc { get; set; }
}

/// <summary>
/// Row DTO for the recent-audit display on <c>/SystemAdmin</c>. Pulled
/// from <see cref="Models.SystemAdminGrantAudit"/> with denormalized
/// names for human read.
/// </summary>
public class SystemAdminAuditRow
{
    public int Id { get; set; }
    public string ActorDisplayName { get; set; } = "";
    public string TargetDisplayName { get; set; } = "";
    public Models.SystemAdminAuditAction Action { get; set; }
    public bool Success { get; set; }
    public string? Reason { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public interface ISystemAdminManagementService
{
    /// <summary>
    /// Grant the SystemAdmin role to <paramref name="targetUserId"/>.
    /// Gated: the caller must themself be a SystemAdmin. Idempotent in
    /// a refused sense — already-has-role returns AlreadyHasRole rather
    /// than silently re-granting. Every call (success or failure) lands
    /// a <see cref="Models.SystemAdminGrantAudit"/> row.
    /// </summary>
    Task<SystemAdminGrantResult> GrantAsync(
        string? actorUserId,
        string targetUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke the SystemAdmin role from <paramref name="targetUserId"/>.
    /// Gated: the caller must themself be a SystemAdmin. Self-revoke is
    /// refused with SelfRevokeRefused — a SysAdmin cannot accidentally
    /// lock themselves out of the role. Does-not-have-role and
    /// self-revoke both refuse; every call lands an audit row.
    /// </summary>
    Task<SystemAdminRevokeResult> RevokeAsync(
        string? actorUserId,
        string targetUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Read-only roster of every SystemAdmin user with their email and
    /// Person display name. Used by <c>/SystemAdmin</c> to render the
    /// "current SystemAdmins" table.
    /// </summary>
    Task<IReadOnlyList<SystemAdminRow>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Most recent <paramref name="count"/> audit rows, newest-first.
    /// Used by <c>/SystemAdmin</c> to render the operational log at the
    /// bottom of the page. Default 50 keeps the page tight; an
    /// audit-trail export endpoint would expose a larger window.
    /// </summary>
    Task<IReadOnlyList<SystemAdminAuditRow>> ListRecentAuditsAsync(
        int count = 50,
        CancellationToken ct = default);
}
