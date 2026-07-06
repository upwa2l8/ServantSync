using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to create a stub <see cref="Person"/></summary>
public enum StubCreationResult
{
    /// <summary>The stub was created. <see cref="StubCreationOutcome.Person"/> + <see cref="StubCreationOutcome.RawToken"/> are populated.</summary>
    Succeeded,

    /// <summary>The caller is not an Admin of the target organization.</summary>
    PermissionDenied,

    /// <summary>The inputs were invalid (empty names, etc).</summary>
    ValidationFailed,

    /// <summary>The target organization does not exist.</summary>
    OrgNotFound,

    /// <summary>Another stub (or another non-stub Person with the same email) already uses this email; rejected so the email-match secondary flow can't accidentally link to the wrong record.</summary>
    EmailCollision,
}

/// <summary>Outcome of an attempt to rotate a stub's claim token.</summary>
public enum TokenRotationResult
{
    /// <summary>The token was rotated. <see cref="TokenRotationOutcome.RawToken"/> is populated with the NEW token.</summary>
    Succeeded,

    /// <summary>The caller is not an Admin of the target organization.</summary>
    PermissionDenied,

    /// <summary>The target <see cref="Person.UserId"/> does not exist.</summary>
    NotFound,

    /// <summary>The target Person exists but isn't a stub (<c>IsStub = false</c>). Real accounts don't have claim tokens.</summary>
    NotAStub,

    /// <summary>The target stub has no active claim token (previous token already consumed / rotated / expired). Admin must regenerate the token (which IS what rotation does — see NotAStub vs NoActiveToken distinction in the implementation comments). Surfacing <c>NoActiveToken</c> lets the page distinguish "no token to rotate" from "target isn't a stub at all".</summary>
    NoActiveToken,
}

/// <summary>Outcome of an attempt to claim a stub Person at registration time.</summary>
public enum StubClaimResult
{
    /// <summary>The stub was successfully merged into the new IdentityUser. <see cref="StubClaimOutcome.MergedPerson"/> is populated.</summary>
    Succeeded,

    /// <summary>Catch-all for empty/whitespace rawClaimToken / newEmail / newIdentityUserId inputs.</summary>
    ValidationFailed,

    /// <summary>The raw token is not a 32-byte Base64Url-decodable string, OR no token hash matches the supplied raw token.</summary>
    InvalidToken,

    /// <summary>The matching token has expired (<c>ExpiresUtc &lt; UtcNow</c>) but has not been claimed / rotated. Admin must rotate to extend.</summary>
    Expired,

    /// <summary>The matching token's <c>ClaimedUtc</c> is already set (consumed OR rotated — round 1 repurposes the field as "terminal state"; see <c>PersonClaimToken.ClaimedUtc</c>).</summary>
    AlreadyClaimed,

    /// <summary>Edge case defense: the matching token's stub Person has already been re-parented (<c>IsStub = false</c>). Shouldn't happen if <c>AlreadyClaimed</c> is set but pin the invariant explicitly.</summary>
    AlreadyLinked,
}

/// <summary>
/// Return shape for <see cref="IPersonService.CreateStubAsync"/>. On
/// <see cref="StubCreationResult.Succeeded"/>, both <see cref="Person"/>
/// and <see cref="RawToken"/> are populated. The raw token is the ONLY
/// time the admin will ever see this string — it is NEVER recoverable
/// from the database (only the SHA-256 hash is persisted).
/// </summary>
public class StubCreationOutcome
{
    public StubCreationResult Result { get; set; }
    public Person? Person { get; set; }
    public string? RawToken { get; set; }
}

/// <summary>
/// Return shape for <see cref="IPersonService.RotateClaimTokenAsync"/>.
/// On success, <see cref="RawToken"/> is the NEW token (the old token's
/// <c>ClaimedUtc</c> has been set to mark it terminal).
/// </summary>
public class TokenRotationOutcome
{
    public TokenRotationResult Result { get; set; }
    public string? RawToken { get; set; }
}

/// <summary>
/// Return shape for <see cref="IPersonService.ClaimStubAsync"/>. On
/// success, <see cref="MergedPerson"/> is the same row re-parented to
/// the new IdentityUser (<c>UserId = newIdentityUserId</c>,
/// <c>IsStub = false</c>, <c>Email = newEmail</c>).
/// </summary>
public class StubClaimOutcome
{
    public StubClaimResult Result { get; set; }
    public Person? MergedPerson { get; set; }
}

/// <summary>
/// One row in the admin "stub members" list. Carries enough state for
/// the page to render a status badge (Active token / Claimed / No
/// token / Expired token).
/// </summary>
/// <param name="PersonUserId">The Person's UserId (FK to IdentityUser.Id).</param>
/// <param name="DisplayName">First + last name, the same shape as <see cref="Person.DisplayName"/>.</param>
/// <param name="Email">The stub's optional email (the email-match secondary claim flow target).</param>
/// <param name="HasActiveToken">True iff there is any PersonClaimToken row for this stub where <c>ClaimedUtc IS NULL</c> AND <c>ExpiresUtc &gt; UtcNow</c>. False for "no token yet", "already consumed", "rotated", or "expired".</param>
/// <param name="TokenExpiresUtc">When the active token (if any) expires. Null if <see cref="HasActiveToken"/> is false.</param>
/// <param name="ClaimedUtc">When the stub's most-recent token was claimed or rotated. Null if the stub has never claimed or rotated (e.g. fresh stub with no token yet, or stale stub whose token expired).</param>
public record StubListItem(
    string PersonUserId,
    string DisplayName,
    string? Email,
    bool HasActiveToken,
    DateTime? TokenExpiresUtc,
    DateTime? ClaimedUtc);

/// <summary>
/// Round-FR-3 service: the boundary that creates stub
/// <see cref="Person"/> rows (admin manually-added volunteers with no
/// usable login yet), rotates their claim tokens, lists them, and
/// claims them when the volunteer eventually registers at
/// <c>/Account/Register?claim=...</c>.
///
/// Security posture:
/// <list type="bullet">
/// <item><description>The three admin methods (<c>CreateStubAsync</c>,
/// <c>RotateClaimTokenAsync</c>, <c>ListStubsAsync</c>) gate on
/// <see cref="IOrgAuthService.IsOrgAdminAsync"/>. Empty
/// <paramref name="callerUserId"/> → <c>PermissionDenied</c> (IDOR
/// defense).</description></item>
/// <item><description><c>ClaimStubAsync</c> is public by design — the
/// raw token IS the authentication. Any holder can claim. The
/// important gate is that the raw token is 32 bytes of cryptographically
/// random data, unguessable, and DB-stored only as a SHA-256 hash.</description></item>
/// <item><description>Failed claims are uniformly classified
/// (<see cref="StubClaimResult.InvalidToken"/>, <see cref="StubClaimResult.Expired"/>,
/// <see cref="StubClaimResult.AlreadyClaimed"/>) so a brute-force
/// probe can't distinguish "wrong format" from "right format but
/// already claimed" from "right format, expired" — no side channel.</description></item>
/// </list>
/// </summary>
public interface IPersonService
{
    /// <summary>
    /// Admin-only. Creates a placeholder <c>IdentityUser</c> (locked out
    /// permanently) + a stub <see cref="Person"/> row
    /// (<c>IsStub=true</c>) + an <see cref="OrganizationMembership"/>
    /// with <c>Role = Volunteer</c> + a <see cref="PersonClaimToken"/>
    /// (30-day expiry, SHA-256 hash stored, raw token returned once).
    /// The admin should print the raw token from the success outcome
    /// and hand it to the volunteer (paper handoff). The audit note
    /// is written to the membership row.
    /// </summary>
    Task<StubCreationOutcome> CreateStubAsync(
        int organizationId,
        string firstName,
        string lastName,
        string? email,
        string? phone,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Admin-only. Marks the current claim token as terminal (its
    /// <c>ClaimedUtc</c> is set to the rotation timestamp) and creates
    /// a new token for the same stub. The OLD raw token is no longer
    /// claimable after rotation.
    /// </summary>
    Task<TokenRotationOutcome> RotateClaimTokenAsync(
        int organizationId,
        string personUserId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Admin-only. Returns every stub <see cref="Person"/> in the
    /// organization with their current claim-token status (active /
    /// consumed / no token). Real (<c>IsStub = false</c>) People are
    /// excluded.
    /// </summary>
    Task<List<StubListItem>> ListStubsAsync(
        int organizationId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Public (no admin gate). Called by the <c>/Account/Register?claim=...</c>
    /// handler after the volunteer successfully creates a real
    /// <c>IdentityUser</c>. Re-parents the stub's <see cref="Person.UserId"/>
    /// to the new IdentityUser, flips <c>IsStub = false</c>, refreshes
    /// the stub's <see cref="Person.Email"/> with the volunteer's
    /// confirmed email, marks the consumed token's <c>ClaimedUtc</c>
    /// as <c>UtcNow</c>, and writes the audit note in whichever
    /// <see cref="OrganizationMembership"/> row the stub holds in the
    /// calling org (or the first one if the stub is in multiple orgs).
    /// </summary>
    Task<StubClaimOutcome> ClaimStubAsync(
        string rawClaimToken,
        string newIdentityUserId,
        string newEmail,
        CancellationToken ct = default);
}
