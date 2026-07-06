namespace ServantSync.Models;

/// <summary>
/// Audit row for SystemAdmin grant and revoke events. Append-only —
/// never updated or deleted by application code so the log is a
/// tamper-evident historical record. Every successful AND failed
/// grant/revoke attempt lands a row so ops can investigate who tried
/// what.
/// </summary>
public class SystemAdminGrantAudit
{
    public int Id { get; set; }

    /// <summary>Identity GUID of the user who initiated the grant/revoke (the caller).</summary>
    public string ActorUserId { get; set; } = "";

    /// <summary>Identity GUID of the user whose role was being granted or revoked (the target).</summary>
    public string TargetUserId { get; set; } = "";

    /// <summary>Action being attempted; stored as int via the <see cref="SystemAdminAuditAction"/> enum.</summary>
    public SystemAdminAuditAction Action { get; set; }

    /// <summary>Whether the action succeeded. Failed attempts land rows with a Reason so the operator can see
    /// why a SysAdmin was unable to grant/revoke.</summary>
    public bool Success { get; set; }

    /// <summary>On failure: a short machine-readable reason ("PermissionDenied", "TargetUserNotFound",
    /// "AlreadyHasRole", "DoesNotHaveRole", "SelfRevokeRefused"). Null on success — the action enum and
    /// success bool already convey the operation's shape.</summary>
    public string? Reason { get; set; }

    /// <summary>UTC timestamp of the event; indexed DESC for the "recent 50 entries" page query.</summary>
    public DateTime TimestampUtc { get; set; }
}
