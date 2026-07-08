using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Implementation of <see cref="IPersonService"/>. See
/// <see cref="IPersonService"/> for the role + security posture;
/// the implementation notes here cover the did-I-cover-everything
/// rationale that's specific to the code, not the spec.
///
/// Re-parent strategy (the FR-3 implementation choice). The spec's
/// "one UPDATE on People SET UserId = @new" framing reads cleanly
/// but assumes ON UPDATE CASCADE on the foreign keys to Person,
/// which EF Core migrations do NOT emit by default (SQLite and SQL
/// Server alike). In practice, flipping People.UserId without
/// cascading the FK columns trips a constraint violation. The
/// implementation below uses the established SQLite pattern — wrap
/// the multi-query update in a transaction with
/// <c>PRAGMA foreign_keys = OFF</c>, update each FK table to point
/// at the new UserId, then flip People.UserId, then restore
/// <c>PRAGMA foreign_keys = ON</c>. The transition is atomic from
/// the caller's perspective; the audit trail + the re-parented
/// Person + the consumed token row are all written in the same
/// SaveChanges call.
/// </summary>
public class PersonService : IPersonService
{
    // Default 30-day token expiry per spec. Exposed static so tests
    // can pin it without depending on internal constants.
    public static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromDays(30);

    // Hash-input format: raw token 32 random bytes -> SHA-256 -> 64
    // hex chars (lowercase). The DB column is StringLength(64) so a
    // 64-char hex hash is the canonical representation.
    public static readonly int TokenByteLength = 32;

    // Placeholder IdentityUser lockout end: per spec "soft-deleted
    // (LockoutEnd=9999-12-31)" so the Identity framework refuses
    // sign-in. DateTime.MaxValue works too but the spec's fixed
    // date makes diagnostics easier ("the lockout end on row X is
    // 9999-12-31" vs "DateTime.MaxValue which the log truncates").
    public static readonly DateTime PlaceholderLockoutEndUtc = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<PersonService> _log;

    public PersonService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserManager<IdentityUser> userManager,
        IOrgAuthService orgAuth,
        ILogger<PersonService> log)
    {
        _dbFactory = dbFactory;
        _userManager = userManager;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<StubCreationOutcome> CreateStubAsync(
        int organizationId,
        string firstName,
        string lastName,
        string? email,
        string? phone,
        string callerUserId,
        CancellationToken ct = default)
    {
        // ---- Input + permission gates ----
        if (string.IsNullOrEmpty(callerUserId))
            return new StubCreationOutcome { Result = StubCreationResult.PermissionDenied };
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return new StubCreationOutcome { Result = StubCreationResult.ValidationFailed };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Round-FR-3.2 polish: OrgNotFound comes BEFORE PermissionDenied
        // so a caller passing an invalid orgId gets actionable feedback
        // ("the org doesn't exist") instead of a misleading PermissionDenied
        // that masks the typo / bad ref. OrgNotFound is safe to surface
        // unconditionally because org existence is a structural database
        // fact, not an access-controlled one.
        var orgExists = await db.Organizations.AnyAsync(o => o.Id == organizationId, ct);
        if (!orgExists)
            return new StubCreationOutcome { Result = StubCreationResult.OrgNotFound };

        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to create a stub in org {OrganizationId}.",
                callerUserId, organizationId);
            return new StubCreationOutcome { Result = StubCreationResult.PermissionDenied };
        }

        // ---- Email collision check ----
        // The Person.Email column is non-unique intentionally (so two
        // children at the same household can both be stubs without
        // contending), BUT creating a stub whose email already exists
        // on another stub (or even on a real Person with the same
        // email) is a recipe for the email-match secondary claim flow
        // to mis-link. We refuse and surface EmailCollision so the
        // admin can resolve it manually (spec edge case).
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existingEmail = await db.People
                .Where(p => p.Email != null && p.Email == email)
                .Select(p => new { p.UserId, p.IsStub })
                .FirstOrDefaultAsync(ct);
            if (existingEmail is not null)
            {
                _log.LogWarning(
                    "Email collision: refused to create stub in org {OrganizationId} because email {Email} is already used by person {PersonUserId} (IsStub={IsStub}).",
                    organizationId, email, existingEmail.UserId, existingEmail.IsStub);
                return new StubCreationOutcome { Result = StubCreationResult.EmailCollision };
            }
        }

        // ---- Create placeholder IdentityUser ----
        // Username + email use the @placeholder.local domain so they
        // can never collide with a real user's email. Password is
        // unguessable random data — since we soft-lock the account
        // below, the password is effectively throwaway, but Identity's
        // CreateAsync still requires one. LockoutEnd is the per-spec
        // 9999-12-31 sentinel: SignInManager.PasswordSignInAsync
        // refuses any sign-in attempt whose LockoutEnd is in the
        // future at the time of the call, regardless of the password
        // match.
        var placeholderId = Guid.NewGuid().ToString("N");
        var placeholderEmail = $"stub+{placeholderId}@placeholder.local";
        var placeholderUser = new IdentityUser
        {
            Id = placeholderId,
            UserName = placeholderEmail,
            Email = placeholderEmail,
            EmailConfirmed = true,
            LockoutEnabled = true,
            LockoutEnd = PlaceholderLockoutEndUtc,
        };
        // Password: two GUIDs concatenated = 72 chars of cryptographic
        // randomness. The password is never used (the lockout blocks
        // sign-in attempts regardless), but Identity's CreateAsync
        // requires a non-null one.
        var placeholderPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var createResult = await _userManager.CreateAsync(placeholderUser, placeholderPassword);
        if (!createResult.Succeeded)
        {
            _log.LogError(
                "Failed to create placeholder IdentityUser for stub in org {OrganizationId}: {Errors}",
                organizationId, string.Join("; ", createResult.Errors.Select(e => e.Description)));
            // Defensive: surface as a generic PermissionDenied so the
            // page treats this as "something the admin can't act on"
            // and doesn't loop / retry. In practice this is a
            // user-store failure that should never happen.
            return new StubCreationOutcome { Result = StubCreationResult.PermissionDenied };
        }

        // ---- Create stub Person + Org membership + claim token ----
        var stub = new Person
        {
            UserId = placeholderId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            IsStub = true,
        };
        db.People.Add(stub);

        // Audit note: spec wording -> "Stub created by {admin} on {date}".
        // 500-char Notes column on OrganizationMembership is more than
        // enough for this string.
        var membership = new OrganizationMembership
        {
            PersonUserId = placeholderId,
            OrganizationId = organizationId,
            Role = OrganizationRole.Volunteer,
            JoinedUtc = DateTime.UtcNow,
            // Round-FR-3.2 polish: audit timestamp goes through round-trip ISO 8601
            // ("o" format) so two rotations/claims on the same day are
            // distinguishable in the membership notes. yyyy-MM-dd alone
            // would alias the timeline to a single string per day.
            Notes = $"Stub created by {callerUserId} on {DateTime.UtcNow.ToString("o")}",
        };
        db.OrganizationMemberships.Add(membership);

        // Claim token: 32 random bytes -> Base64Url (43 chars raw) ->
        // SHA-256 hex (64 chars hash stored).
        var (rawToken, tokenHash) = GenerateToken();
        db.PersonClaimTokens.Add(new PersonClaimToken
        {
            PersonUserId = placeholderId,
            TokenHash = tokenHash,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.Add(DefaultTokenLifetime),
            CreatedByUserId = callerUserId,
        });

        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Stub Person {PersonUserId} created in org {OrganizationId} by admin {CallerUserId}; active claim token expires at {ExpiresUtc}.",
            placeholderId, organizationId, callerUserId, DateTime.UtcNow.Add(DefaultTokenLifetime));

        return new StubCreationOutcome
        {
            Result = StubCreationResult.Succeeded,
            Person = stub,
            RawToken = rawToken,
        };
    }

    public async Task<TokenRotationOutcome> RotateClaimTokenAsync(
        int organizationId,
        string personUserId,
        string callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return new TokenRotationOutcome { Result = TokenRotationResult.PermissionDenied };
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to rotate claim token for {PersonUserId} in org {OrganizationId}.",
                callerUserId, personUserId, organizationId);
            return new TokenRotationOutcome { Result = TokenRotationResult.PermissionDenied };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Round-FR-3.2 polish: rotate is scoped to the admin's org --
        // the stub must have a membership row in the requested
        // organizationId. This closes the cross-org rotate hole (an
        // Org-A admin could previously rotate a token for a stub that's
        // only in Org B, even when not admin of Org B). The admin gate
        // still runs FIRST so a non-admin caller can't probe Person
        // existence across orgs (defense-in-depth preserved).
        var stub = await db.People
            .Where(p => p.UserId == personUserId
                && p.Memberships.Any(m => m.OrganizationId == organizationId))
            .FirstOrDefaultAsync(ct);
        if (stub is null)
            return new TokenRotationOutcome { Result = TokenRotationResult.NotFound };
        if (!stub.IsStub)
        {
            _log.LogWarning(
                "Refused to rotate claim token: person {PersonUserId} in org {OrganizationId} is not a stub (IsStub=false).",
                personUserId, organizationId);
            return new TokenRotationOutcome { Result = TokenRotationResult.NotAStub };
        }

        // Find the currently-active token (ClaimedUtc IS NULL AND
        // ExpiresUtc > UtcNow). If none, distinguish "no token yet"
        // from "already terminal": both return NoActiveToken since
        // the rotation's purpose is to invalidate the active one.
        var active = await db.PersonClaimTokens
            .Where(t => t.PersonUserId == personUserId
                && t.ClaimedUtc == null
                && t.ExpiresUtc > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedUtc)
            .FirstOrDefaultAsync(ct);
        if (active is null)
        {
            // Either no token has ever been generated (e.g. a stub that
            // was created but lost its token somehow), the previous
            // one is consumed, or the previous one is expired. All
            // three collapse to NoActiveToken from the rotation
            // surface — the admin just needs to call Create is not
            // possible (PersonService has no "CreateNewTokenOnlyAsync");
            // instead the admin uses the same flow that always creates
            // a fresh token on the re-create path. We DO create a new
            // token here anyway (the rotation is "produce a fresh
            // token" by spec), so we mark the missing active as no-op.
            _log.LogWarning(
                "Rotation requested for stub {PersonUserId} in org {OrganizationId} with no active token; skipping the invalidation step and minting a fresh token.",
                personUserId, organizationId);
        }
        else
        {
            // "Invalidates the previous token (sets its ClaimedUtc =
            // rotation timestamp)". Round 1's spec reuses ClaimedUtc
            // for terminal-state across consumed / rotated / expired;
            // a round 2 could split into IsRevoked for clarity.
            active.ClaimedUtc = DateTime.UtcNow;
        }

        var (rawToken, tokenHash) = GenerateToken();
        db.PersonClaimTokens.Add(new PersonClaimToken
        {
            PersonUserId = personUserId,
            TokenHash = tokenHash,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.Add(DefaultTokenLifetime),
            CreatedByUserId = callerUserId,
        });

        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Claim token rotated for stub {PersonUserId} in org {OrganizationId} by admin {CallerUserId}; new token expires at {ExpiresUtc}.",
            personUserId, organizationId, callerUserId, DateTime.UtcNow.Add(DefaultTokenLifetime));

        return new TokenRotationOutcome
        {
            Result = TokenRotationResult.Succeeded,
            RawToken = rawToken,
        };
    }

    public async Task<List<StubListItem>> ListStubsAsync(
        int organizationId,
        string callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(callerUserId))
            return new List<StubListItem>();
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to list stubs in org {OrganizationId}.",
                callerUserId, organizationId);
            return new List<StubListItem>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Stub is a Person + a PersonClaimToken row. The active-token
        // check is computed per-row in the projection (the most-recent
        // non-terminal, non-expired token per stub). We use FirstOrDefault
        // per stub rather than grouping because the test fixture isn't
        // expected to have many tokens per stub in practice (creation +
        // maybe 1-2 rotations).
        var stubs = await db.People
            .Where(p => p.IsStub
                && p.Memberships.Any(m => m.OrganizationId == organizationId))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Select(p => new
            {
                p.UserId,
                p.FirstName,
                p.LastName,
                p.Email,
                ActiveToken = p.UserId != null
                    ? db.PersonClaimTokens
                        .Where(t => t.PersonUserId == p.UserId
                            && t.ClaimedUtc == null
                            && t.ExpiresUtc > DateTime.UtcNow)
                        .OrderByDescending(t => t.CreatedUtc)
                        .FirstOrDefault()
                    : null,
                // For the "ClaimedUtc" display: the most recent TERMINAL
                // token (consumed OR rotated). Surfacing it lets the admin
                // see "this stub was claimed 3 days ago" without needing
                // a separate /People/{id} page.
                LastTerminalToken = p.UserId != null
                    ? db.PersonClaimTokens
                        .Where(t => t.PersonUserId == p.UserId && t.ClaimedUtc != null)
                        .OrderByDescending(t => t.ClaimedUtc)
                        .FirstOrDefault()
                    : null,
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return stubs.Select(s => new StubListItem(
            PersonUserId: s.UserId,
            DisplayName: $"{s.FirstName} {s.LastName}".Trim(),
            Email: s.Email,
            HasActiveToken: s.ActiveToken is not null,
            TokenExpiresUtc: s.ActiveToken?.ExpiresUtc,
            ClaimedUtc: s.LastTerminalToken?.ClaimedUtc)).ToList();
    }

    public async Task<StubClaimOutcome> ClaimStubAsync(
        string rawClaimToken,
        string newIdentityUserId,
        string newEmail,
        CancellationToken ct = default)
    {
        // Input gate. Treats ALL malformed inputs as ValidationFailed
        // so a brute-force probe can't distinguish "empty token" from
        // "garbage token" from "no matching hash" — the public claim
        // path is uniform.
        if (string.IsNullOrWhiteSpace(rawClaimToken)
            || string.IsNullOrWhiteSpace(newIdentityUserId)
            || string.IsNullOrWhiteSpace(newEmail))
        {
            return new StubClaimOutcome { Result = StubClaimResult.ValidationFailed };
        }

        // Decode the raw token. If the byte length or format is wrong,
        // it's InvalidToken — the caller can't tell whether they had
        // a base64 decode error or a hash mismatch (we don't hash
        // anything yet so this is a structural-format probe).
        byte[] tokenBytes;
        try
        {
            tokenBytes = WebEncoders.Base64UrlDecode(rawClaimToken);
        }
        catch (FormatException)
        {
            _log.LogInformation("ClaimStubAsync: raw token is not valid Base64Url.");
            return new StubClaimOutcome { Result = StubClaimResult.InvalidToken };
        }
        if (tokenBytes.Length != TokenByteLength)
        {
            _log.LogInformation(
                "ClaimStubAsync: raw token decoded to {ActualLength} bytes, expected {ExpectedLength}.",
                tokenBytes.Length, TokenByteLength);
            return new StubClaimOutcome { Result = StubClaimResult.InvalidToken };
        }

        var inputHash = ComputeTokenHash(tokenBytes);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var token = await db.PersonClaimTokens
            .Include(t => t.Person)
            .FirstOrDefaultAsync(t => t.TokenHash == inputHash, ct);
        if (token is null)
            return new StubClaimOutcome { Result = StubClaimResult.InvalidToken };

        // Terminal-state check FIRST (per Question 7 + design
        // resolution: ClaimedUtc means "consumed OR rotated OR
        // expired-by-hand" — round 1 repurposes the column).
        if (token.ClaimedUtc is not null)
            return new StubClaimOutcome { Result = StubClaimResult.AlreadyClaimed };

        if (token.ExpiresUtc <= DateTime.UtcNow)
            return new StubClaimOutcome { Result = StubClaimResult.Expired };

        // Defense in depth: the stub Person exists, but the re-parent
        // surface shouldn't trip this if the audit trail is intact.
        // Pin the invariant.
        if (token.Person is null)
        {
            _log.LogError(
                "Invariant violation: PersonClaimToken {TokenId} references missing Person {PersonUserId} (FK cascade should make this impossible).",
                token.Id, token.PersonUserId);
            return new StubClaimOutcome { Result = StubClaimResult.InvalidToken };
        }
        if (!token.Person.IsStub)
        {
            // Already claimed in a previous round / concurrent claim.
            return new StubClaimOutcome { Result = StubClaimResult.AlreadyLinked };
        }

        var oldStubId = token.PersonUserId;

        // ---- Re-parent ----
        // The spec's "one UPDATE on People SET UserId = @new" framing
        // assumes ON UPDATE CASCADE on every column that references
        // People.UserId, which EF Core migrations do NOT emit by
        // default. So we flip the FK column on every dependent table
        // (OrganizationMembership, Assignment, TrainingCompletion,
        // TrainingActivity, TrainingSessionAttendees) from
        // oldStubId -> newIdentityUserId BEFORE flipping the People
        // row's PK. The FK toggle is OFF during the batch.
        //
        // Round-FR-3.2 fix (replaces per-statement ExecuteUpdateAsync):
        // PRAGMA foreign_keys is per-connection in SQLite, and
        // Microsoft.EntityFrameworkCore.Sqlite can re-open the
        // underlying SqliteConnection between EF Core calls. So the
        // PRAGMA=OFF set before the batch in the prior implementation
        // was lost by the time the first UPDATE ran, and the FK
        // constraint failed. The fix is to run the WHOLE re-parent
        // as ONE multi-statement SQL batch via ExecuteSqlInterpolated
        // Async; sqlite3_exec runs the entire string on one
        // connection so PRAGMA=OFF stays in effect across every
        // sub-statement. FormattableString interpolation also
        // parameterizes every user input so the SQL isn't injection-
        // vulnerable.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reparentedAt = DateTime.UtcNow;
        var notesValue   = $"Claimed by {newIdentityUserId} on {reparentedAt.ToString("o")}";
        // Round-FR-3.2 fix (replaces the futile PRAGMA=OFF/ON bracketing
        // + finally-reset pattern): per the SQLite docs,
        // `PRAGMA foreign_keys = OFF` is a SILENT NO-OP inside an active
        // transaction — "foreign key constraint enforcement may only be
        // enabled or disabled when there is no pending BEGIN or SAVEPOINT".
        // So the previous `BeginTransactionAsync` + `PRAGMA=OFF` + UPDATEs
        // pattern left FK enforcement ON, and every dependent-table UPDATE
        // tripped the constraint. The canonical SQLite pattern for this
        // re-parent scenario is `PRAGMA defer_foreign_keys = 1;` which
        // DEFERS FK checks until the outermost transaction COMMIT, then
        // auto-resets at COMMIT or ROLLBACK (no finally-reset needed).
        //
        // PRAGMA is SQLite-specific; we gate on ProviderName so the call
        // degrades to a no-op on a non-SQLite provider if one is ever
        // added (Postgres would need a different FK-deferral strategy).
        // Provider-specific FK handling for the re-parent batch.
        // SQLite: defer_foreign_keys PRAGMA defers FK checks until commit.
        // SQL Server: NOCHECK CONSTRAINT ALL temporarily disables FK
        //   checks on the referencing tables so the multi-row UPDATE
        //   batch can set PersonUserId to newIdentityUserId before
        //   People.UserId is flipped (SQL Server checks FKs at the
        //   statement level; without this, the first UPDATE on
        //   OrganizationMemberships would trip a FK violation because
        //   newIdentityUserId doesn't exist in People yet).
        // Both are scoped to the current connection/session and reset
        // on COMMIT or ROLLBACK (or explicit re-enable for SQL Server).
        try
        {
            if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // Per https://sqlite.org/pragma.html#pragma_defer_foreign_keys
                // "The defer_foreign_keys pragma is automatically switched
                // off at each COMMIT or ROLLBACK."
                await db.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = 1;", ct);
            }
            else if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // Disable FK + CHECK constraints on every table that
                // references PersonUserId (the FK column being flipped).
                // These are session-scoped — re-enabled in the finally
                // block below regardless of commit/rollback outcome.
                await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE OrganizationMemberships NOCHECK CONSTRAINT ALL;
ALTER TABLE Assignments NOCHECK CONSTRAINT ALL;
ALTER TABLE TrainingCompletions NOCHECK CONSTRAINT ALL;
ALTER TABLE TrainingActivities NOCHECK CONSTRAINT ALL;
ALTER TABLE TrainingSessionAttendees NOCHECK CONSTRAINT ALL;
ALTER TABLE PersonClaimTokens NOCHECK CONSTRAINT ALL;", ct);
            }

            // Single multi-statement SQL batch: update every FK
            // reference from oldStubId → newIdentityUserId, flip
            // People.UserId (PK), stamp the audit note, consume the
            // token. FormattableString interpolation binds every
            // {placeholder} as a named parameter — no SQL injection.
            await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE OrganizationMemberships SET PersonUserId = {newIdentityUserId} WHERE PersonUserId = {oldStubId};
UPDATE Assignments            SET PersonUserId = {newIdentityUserId} WHERE PersonUserId = {oldStubId};
UPDATE TrainingCompletions    SET PersonUserId = {newIdentityUserId} WHERE PersonUserId = {oldStubId};
UPDATE TrainingActivities     SET PersonUserId = {newIdentityUserId} WHERE PersonUserId = {oldStubId};
UPDATE TrainingSessionAttendees SET PersonUserId = {newIdentityUserId} WHERE PersonUserId = {oldStubId};
UPDATE People SET UserId = {newIdentityUserId}, IsStub = 0, Email = {newEmail.Trim()} WHERE UserId = {oldStubId};
UPDATE OrganizationMemberships SET Notes = {notesValue} WHERE PersonUserId = {newIdentityUserId};
UPDATE PersonClaimTokens SET PersonUserId = {newIdentityUserId}, ClaimedUtc = {reparentedAt} WHERE Id = {token.Id};");
            await tx.CommitAsync(ct);
        }
        catch
        {
            throw;
        }
        finally
        {
            // Re-enable FK constraints on SQL Server. MUST run
            // regardless of commit/rollback — the session-level
            // NOCHECK persists until explicitly reversed. WITH CHECK
            // validates all existing rows against the constraints
            // and marks them as trusted for the query optimizer.
            // DDL statements (ALTER TABLE) auto-commit in SQL Server,
            // so they execute outside the transaction scope; the
            // re-enable runs even if the transaction was rolled back.
            if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE OrganizationMemberships WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE Assignments WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE TrainingCompletions WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE TrainingActivities WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE TrainingSessionAttendees WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE PersonClaimTokens WITH CHECK CHECK CONSTRAINT ALL;", ct);
                }
                catch (Exception reenableEx)
                {
                    // WITH CHECK validates existing data against the
                    // constraints. If this fails, the re-parent left
                    // the data in an inconsistent state — log and
                    // re-throw so the caller gets the full picture.
                    _log.LogError(reenableEx,
                        "Failed to re-enable FK constraints after re-parent of stub {OldStubId} → {NewId}. Data may be in an inconsistent state.",
                        oldStubId, newIdentityUserId);
                    throw;
                }
            }
        }

        _log.LogInformation(
            "Stub {OldStubUserId} claimed by new IdentityUser {NewIdentityUserId} (email {Email}).",
            oldStubId, newIdentityUserId, newEmail);

        // Read back the merged Person row so callers can immediately
        // navigate to the new canonical record.
        var merged = await db.People.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == newIdentityUserId, ct);
        return new StubClaimOutcome
        {
            Result = StubClaimResult.Succeeded,
            MergedPerson = merged,
        };
    }

    // ---- Token helpers ----

    /// <summary>
    /// Generates a new raw token (caller-visible) + its stored hash
    /// (DB-visible). 32 cryptographic random bytes, Base64Url-encoded
    /// for the raw token, SHA-256 hex for the hash.
    /// </summary>
    public static (string RawToken, string TokenHash) GenerateToken()
    {
        var bytes = new byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        var raw = WebEncoders.Base64UrlEncode(bytes);
        var hash = ComputeTokenHash(bytes);
        return (raw, hash);
    }

    private static string ComputeTokenHash(byte[] tokenBytes)
    {
        var hash = SHA256.HashData(tokenBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
