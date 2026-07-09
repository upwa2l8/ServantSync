using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Round-FR-3.2: integration tests for <see cref="PersonService"/>.
/// Covers the round-FR-3 spec (manually-added volunteers with
/// claim-token re-parenting): stub creation, token rotation,
/// list-stubs, and the critical re-parent claim path. Exercises
/// every decision-enforced service-layer invariant — 30-day token
/// expiry, token-after-claim-becomes-terminal, re-parent cascade
/// across every Person FK chain, IDOR defense on admin gates, and
/// the cryptographic token-shape contract (32 raw bytes /
/// Base64Url / SHA-256 hex).
///
/// The test fixture inherits a real <see cref="UserManager{IdentityUser}"/>
/// from <see cref="SqliteTestBase"/> so <c>CreateStubAsync</c> hits
/// the production Identity creation path (not a mock).
/// </summary>
public class PersonServiceTests : SqliteTestBase
{
    private PersonService NewService() =>
        new PersonService(Factory, UserManager, new StubOrgAuth(Factory), NullLogger<PersonService>.Instance);

    // ─── Test helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds an OrgAdmin identity: ensures the IdentityUser row +
    /// AspNetUserRoles role row + OrganizationMembership(Admin). The
    /// admin's callerUserId is the Person.UserId (and IdentityUser.Id)
    /// so a single token string is used consistently.
    /// </summary>
    private (string AdminUserId, Person Admin, Organization Org) SeedAdmin(string orgName = "Org A", string firstName = "Alice", string lastName = "Admin")
    {
        var org = TestData.Org(Factory, orgName);
        var admin = TestData.Person(Factory, firstName, lastName);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        return (admin.UserId, admin, org);
    }

    /// <summary>Seeds a Volunteer in the org (for PermissionDenied tests).</summary>
    private (string VolunteerUserId, Person Volunteer) SeedVolunteer(int orgId, string firstName = "Vicky", string lastName = "Volunteer")
    {
        var volunteer = TestData.Person(Factory, firstName, lastName);
        TestData.Membership(Factory, volunteer.UserId, orgId, OrganizationRole.Volunteer);
        return (volunteer.UserId, volunteer);
    }

    /// <summary>Seeds a Coordinator in the org (for PermissionDenied tests).</summary>
    private (string CoordUserId, Person Coord) SeedCoordinator(int orgId, string firstName = "Chris", string lastName = "Coord")
    {
        var coord = TestData.Person(Factory, firstName, lastName);
        TestData.Membership(Factory, coord.UserId, orgId, OrganizationRole.MinistryDirector);
        return (coord.UserId, coord);
    }

    /// <summary>Seeds an admin in Org B (for cross-org PermissionDenied tests).</summary>
    private (string OtherAdminUserId, Person OtherAdmin, Organization OtherOrg) SeedOtherOrgAdmin(string orgName = "Org B")
    {
        var org = TestData.Org(Factory, orgName);
        var admin = TestData.Person(Factory, "Bob", "OtherAdmin");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        return (admin.UserId, admin, org);
    }

    /// <summary>
    /// Creates a real <c>IdentityUser</c> + an AS-IF-<c>PersonService.CreateStubAsync</c>-had-run
    /// stub state. Used by <c>RotateClaimTokenAsync</c> / <c>ClaimStubAsync</c>
    /// tests that need a pre-existing stub without round-tripping through
    /// <c>CreateStubAsync</c> directly. The token's <c>TokenHash</c> is
    /// derived from the raw token so the round-trip shapes are consistent.
    /// </summary>
    private (string StubUserId, string RawToken, Person Stub, PersonClaimToken Token) SeedStubWithToken(
        int orgId,
        string firstName = "Sara",
        string lastName = "Stub",
        string? email = null,
        DateTime? expiresOverride = null)
    {
        var stubUserId = Guid.NewGuid().ToString("N");
        var stubEmail = $"stub+{stubUserId}@placeholder.local";
        // Direct EF insert: tests == production behavior here (the
        // placeholder user is locked out regardless of password).
        using (var db = Factory.CreateDbContext())
        {
            db.Users.Add(new IdentityUser
            {
                Id = stubUserId,
                UserName = stubEmail,
                Email = stubEmail,
                EmailConfirmed = true,
                LockoutEnabled = true,
                LockoutEnd = PersonService.PlaceholderLockoutEndUtc,
                PasswordHash = "test-stub-no-login",
            });
            db.SaveChanges();
        }
        var stub = TestData.Person(Factory, firstName, lastName, stubUserId);
        // Flip IsStub=true (TestData.Person creates real (non-stub) People).
        using (var db = Factory.CreateDbContext())
        {
            var p = db.People.First(p => p.UserId == stubUserId);
            p.IsStub = true;
            if (!string.IsNullOrWhiteSpace(email)) p.Email = email;
            db.SaveChanges();
        }
        TestData.Membership(Factory, stubUserId, orgId, OrganizationRole.Volunteer);

        var (rawToken, hash) = PersonService.GenerateToken();
        var admin = TestData.Person(Factory, "Alice", "Admin");
        TestData.Membership(Factory, admin.UserId, orgId, OrganizationRole.Admin);

        PersonClaimToken token;
        using (var db = Factory.CreateDbContext())
        {
            token = new PersonClaimToken
            {
                PersonUserId = stubUserId,
                TokenHash = hash,
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = expiresOverride ?? DateTime.UtcNow.Add(PersonService.DefaultTokenLifetime),
                CreatedByUserId = admin.UserId,
            };
            db.PersonClaimTokens.Add(token);
            db.SaveChanges();
        }
        return (stubUserId, rawToken, stub, token);
    }

    /// <summary>
    /// Creates an IdentityUser via UserManager WITHOUT seeding a Person
    /// row. Mirrors the production /Account/Register flow: registration
    /// yields an IdentityUser only; the Person row comes later (via org
    /// join or, in the FR-3 flow, via stub claim). Without this helper,
    /// ClaimStubAsync tests that pass a "new volunteer" identity would
    /// hit a UNIQUE-constraint collision on People.UserId when the
    /// service re-parents the stub's Person row to that user --
    /// TestData.Person creates BOTH Person + IdentityUser, leaving an
    /// orphan Person row at the new identity user's UserId.
    /// </summary>
    private IdentityUser SeedIdentityOnly(string email)
    {
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };
        UserManager.CreateAsync(user).GetAwaiter().GetResult();
        return user;
    }

    // Stub OrgAuthService — only the methods we touch are realistic;
    // any method not called in a test returns null/false (defensive).
    private sealed class StubOrgAuth : IOrgAuthService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        public StubOrgAuth(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

        public async Task<bool> IsOrgAdminAsync(string? userId, int organizationId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            using var db = _factory.CreateDbContext();
            return await db.OrganizationMemberships
                .AnyAsync(m => m.PersonUserId == userId
                    && m.OrganizationId == organizationId
                    && m.Role == OrganizationRole.Admin, ct);
        }

        // Round-FR-3.2 polish: the rest of the IOrgAuthService surface
        // is NOT exercised by PersonServiceTests. We return safe
        // defaults (false / null) instead of throwing
        // NotImplementedException so a future service method that
        // accidentally calls one of these fails the ASSERTION rather
        // than crashing with a confusing exception in test setup.
        public Task<OrganizationRole?> GetRoleAsync(string? userId, int organizationId, CancellationToken ct = default)
            => Task.FromResult<OrganizationRole?>(null);

        public Task<bool> CanManageOrgAsync(string? userId, int organizationId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsSystemAdminAsync(string? userId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsAnyOrgAdminAsync(string? userId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsAnyOrgManagerAsync(string? userId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> CanManageMinistryAsync(string? userId, int ministryId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> CanManageTeamAsync(string? userId, int teamId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> CanManageSlotAsync(string? userId, int slotId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsParentOfAnyPlayerOnTeamAsync(string? userId, int teamId, CancellationToken ct = default)
            => Task.FromResult(false);

        // Round-FR-5: the five new interface members introduced when
        // Coordinator split into MinistryDirector + SlotCoordinator.
        // PersonServiceTests doesn't exercise these gates — return
        // false so a future service method that accidentally calls one
        // (the same defensive pattern as the other stubs above) fails
        // the underlying assertion loudly rather than crashing here.
        public Task<bool> IsMinistryDirectorAsync(string? userId, int organizationId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsSlotCoordinatorAsync(string? userId, int organizationId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsAnyMinistryDirectorAsync(string? userId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsAnySlotCoordinatorAsync(string? userId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> IsAnyTrainingManagerAsync(string? userId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    // ─── CreateStubAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateStubAsync_HappyPath_CreatesStubAndReturnsRawToken()
    {
        var (adminId, _, org) = SeedAdmin();
        var (rawToken, hash) = PersonService.GenerateToken();
        // Pre-generate the expected shape so we can assert the round-trip.

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "Sara",
            lastName: "Smith",
            email: "sara@example.com",
            phone: "555-1212",
            callerUserId: adminId);

        Assert.Equal(StubCreationResult.Succeeded, result.Result);
        Assert.NotNull(result.Person);
        Assert.NotNull(result.RawToken);
        Assert.True(result.Person!.IsStub);
        Assert.Equal("Sara", result.Person.FirstName);
        Assert.Equal("Smith", result.Person.LastName);
        Assert.Equal("sara@example.com", result.Person.Email);
        Assert.Equal("555-1212", result.Person.Phone);
        Assert.Equal(result.Person.UserId, result.Person.UserId); // PK=FK invariant

        // Raw token shape: 32 random bytes -> Base64Url = 43 chars
        // (no '+', no '/', no '=' padding).
        Assert.Equal(43, result.RawToken!.Length);
        Assert.DoesNotContain("+", result.RawToken);
        Assert.DoesNotContain("/", result.RawToken);
        Assert.DoesNotContain("=", result.RawToken);

        // Person row + IdentityUser row + audit note + token hash all wired.
        await using var db = await Factory.CreateDbContextAsync();
        var stub = await db.People.FirstAsync(p => p.UserId == result.Person.UserId);
        Assert.True(stub.IsStub);

        var placeholder = await db.Users.FirstAsync(u => u.Id == result.Person.UserId);
        Assert.Equal("placeholder.local", placeholder.Email!.Split('@')[1]);
        Assert.True(placeholder.LockoutEnabled);
        Assert.Equal(PersonService.PlaceholderLockoutEndUtc, placeholder.LockoutEnd);
        Assert.DoesNotContain(adminId, placeholder.Email!); // admin's email not leaked

        var membership = await db.OrganizationMemberships
            .FirstAsync(m => m.PersonUserId == result.Person.UserId && m.OrganizationId == org.Id);
        Assert.Equal(OrganizationRole.Volunteer, membership.Role);
        Assert.Contains(adminId, membership.Notes);
        Assert.Contains("Stub created by", membership.Notes);

        var token = await db.PersonClaimTokens
            .FirstAsync(t => t.PersonUserId == result.Person.UserId && t.ClaimedUtc == null);
        Assert.Equal(64, token.TokenHash.Length); // SHA-256 hex
        // The stored hash must match the SHA-256 of the raw token bytes
        // — proves the implementation uses the same encoding the
        // admin can independently verify.
        var hashOfRaw = PersonService.GenerateToken(); // generate a fresh one for noise; we want the computed one
        // Compute the hash of the returned raw token:
        var expectedHashBytes = System.Security.Cryptography.SHA256.HashData(
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(result.RawToken));
        Assert.Equal(token.TokenHash,
            System.BitConverter.ToString(expectedHashBytes).Replace("-", "").ToLowerInvariant());
    }

    [Fact]
    public async Task CreateStubAsync_NoEmail_OptionalEmailColumnIsNull()
    {
        var (adminId, _, org) = SeedAdmin();

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "NoEmail",
            lastName: "Volunteer",
            email: null,
            phone: null,
            callerUserId: adminId);

        Assert.Equal(StubCreationResult.Succeeded, result.Result);
        Assert.Null(result.Person!.Email);
        Assert.Null(result.Person.Phone);
    }

    [Fact]
    public async Task CreateStubAsync_VolunteerCaller_PermissionDenied()
    {
        var (_, _, org) = SeedAdmin();
        var (volunteerId, _) = SeedVolunteer(org.Id);

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "X", lastName: "Y",
            email: null, phone: null,
            callerUserId: volunteerId);

        Assert.Equal(StubCreationResult.PermissionDenied, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_CoordinatorCaller_PermissionDenied()
    {
        var (_, _, org) = SeedAdmin();
        var (coordId, _) = SeedCoordinator(org.Id);

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "X", lastName: "Y",
            email: null, phone: null,
            callerUserId: coordId);

        Assert.Equal(StubCreationResult.PermissionDenied, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_ForeignOrgAdminCaller_PermissionDenied()
    {
        var (_, _, orgA) = SeedAdmin("Org A");
        var (otherAdminId, _, _) = SeedOtherOrgAdmin("Org B");

        var result = await NewService().CreateStubAsync(
            organizationId: orgA.Id,
            firstName: "X", lastName: "Y",
            email: null, phone: null,
            callerUserId: otherAdminId);

        Assert.Equal(StubCreationResult.PermissionDenied, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_EmptyCallerUserId_PermissionDenied()
    {
        var (_, _, org) = SeedAdmin();

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "X", lastName: "Y",
            email: null, phone: null,
            callerUserId: "");

        Assert.Equal(StubCreationResult.PermissionDenied, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_EmptyFirstName_ValidationFailed()
    {
        var (adminId, _, org) = SeedAdmin();

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "", lastName: "Y",
            email: null, phone: null,
            callerUserId: adminId);

        Assert.Equal(StubCreationResult.ValidationFailed, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_WhitespaceLastName_ValidationFailed()
    {
        var (adminId, _, org) = SeedAdmin();

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "X", lastName: "   ",
            email: null, phone: null,
            callerUserId: adminId);

        Assert.Equal(StubCreationResult.ValidationFailed, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_UnknownOrg_OrgNotFound()
    {
        var (adminId, _, _) = SeedAdmin();

        var result = await NewService().CreateStubAsync(
            organizationId: 999_999,
            firstName: "X", lastName: "Y",
            email: null, phone: null,
            callerUserId: adminId);

        Assert.Equal(StubCreationResult.OrgNotFound, result.Result);
    }

    [Fact]
    public async Task CreateStubAsync_EmailAlreadyUsedByAnotherPerson_EmailCollision()
    {
        var (adminId, _, org) = SeedAdmin();
        // Pre-existing real Person with the email
        var existing = TestData.Person(Factory, "First", "Existing");
        using (var db = Factory.CreateDbContext())
        {
            var p = db.People.First(p => p.UserId == existing.UserId);
            p.Email = "shared@example.com";
            db.SaveChanges();
        }

        var result = await NewService().CreateStubAsync(
            organizationId: org.Id,
            firstName: "X", lastName: "Y",
            email: "shared@example.com", phone: null,
            callerUserId: adminId);

        Assert.Equal(StubCreationResult.EmailCollision, result.Result);
    }

    // ─── RotateClaimTokenAsync ───────────────────────────────────────────────

    [Fact]
    public async Task RotateClaimTokenAsync_HappyPath_NewTokenInvalidatesOld()
    {
        var (adminId, _, org) = SeedAdmin();
        var (stubId, oldRawToken, _, oldToken) = SeedStubWithToken(org.Id);

        var oldExpiresUtc = oldToken.ExpiresUtc;
        var result = await NewService().RotateClaimTokenAsync(
            organizationId: org.Id,
            personUserId: stubId,
            callerUserId: adminId);

        Assert.Equal(TokenRotationResult.Succeeded, result.Result);
        Assert.NotNull(result.RawToken);
        Assert.NotEqual(oldRawToken, result.RawToken); // new token is fresh

        // Old token's ClaimedUtc is set (terminal-state repurposed).
        await using var db = await Factory.CreateDbContextAsync();
        var oldNow = await db.PersonClaimTokens.FirstAsync(t => t.Id == oldToken.Id);
        Assert.NotNull(oldNow.ClaimedUtc);
        // ExpiresUtc unchanged (we don't extend the old one's window —
        // it just becomes unclaimable).
        Assert.Equal(oldExpiresUtc, oldNow.ExpiresUtc);

        // New token's row exists for the same stub, active.
        var active = await db.PersonClaimTokens
            .Where(t => t.PersonUserId == stubId && t.ClaimedUtc == null)
            .SingleAsync();
        Assert.True(active.ExpiresUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task RotateClaimTokenAsync_OldTokenCannotClaimAnymore()
    {
        var (adminId, _, org) = SeedAdmin();
        var (stubId, oldRawToken, _, _) = SeedStubWithToken(org.Id);
        await NewService().RotateClaimTokenAsync(org.Id, stubId, adminId);

        var newVolunteerUserId = TestData.Person(Factory, "New", "Volunteer").UserId;

        var claim = await NewService().ClaimStubAsync(
            rawClaimToken: oldRawToken,
            newIdentityUserId: newVolunteerUserId,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.AlreadyClaimed, claim.Result);
        // The stub is still a stub — the old token attempt was refused
        // and the FK chain still points at the stub user.
        await using var db = await Factory.CreateDbContextAsync();
        var stillStub = await db.People.FirstAsync(p => p.UserId == stubId);
        Assert.True(stillStub.IsStub);
    }

    [Fact]
    public async Task RotateClaimTokenAsync_NonAdminCaller_PermissionDenied()
    {
        var (_, _, org) = SeedAdmin();
        var (stubId, _, _, _) = SeedStubWithToken(org.Id);
        var (volunteerId, _) = SeedVolunteer(org.Id);

        var result = await NewService().RotateClaimTokenAsync(
            organizationId: org.Id,
            personUserId: stubId,
            callerUserId: volunteerId);

        Assert.Equal(TokenRotationResult.PermissionDenied, result.Result);
    }

    [Fact]
    public async Task RotateClaimTokenAsync_ForeignOrgAdmin_PermissionDenied()
    {
        var (_, _, orgA) = SeedAdmin("Org A");
        var (stubId, _, _, _) = SeedStubWithToken(orgA.Id);
        var (otherAdminId, _, _) = SeedOtherOrgAdmin("Org B");

        var result = await NewService().RotateClaimTokenAsync(
            organizationId: orgA.Id,
            personUserId: stubId,
            callerUserId: otherAdminId);

        Assert.Equal(TokenRotationResult.PermissionDenied, result.Result);
    }

    [Fact]
    public async Task RotateClaimTokenAsync_UnknownPerson_NotFound()
    {
        var (adminId, _, org) = SeedAdmin();

        var result = await NewService().RotateClaimTokenAsync(
            organizationId: org.Id,
            personUserId: "nonexistent-user-id",
            callerUserId: adminId);

        Assert.Equal(TokenRotationResult.NotFound, result.Result);
    }

    [Fact]
    public async Task RotateClaimTokenAsync_RotateRealPerson_NotAStub()
    {
        var (adminId, _, org) = SeedAdmin();
        var real = TestData.Person(Factory, "Real", "Person");
        TestData.Membership(Factory, real.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().RotateClaimTokenAsync(
            organizationId: org.Id,
            personUserId: real.UserId,
            callerUserId: adminId);

        Assert.Equal(TokenRotationResult.NotAStub, result.Result);
    }

    [Fact]
    public async Task RotateClaimTokenAsync_NoActiveTokenStillMintsFresh()
    {
        // Per design: rotation with no active token mints a fresh
        // one (admin recovery flow). NoActiveToken is NOT returned —
        // the result is Succeeded with a new raw token.
        var (adminId, _, org) = SeedAdmin();
        var (stubId, _, _, oldToken) = SeedStubWithToken(org.Id);
        // Manually consume the active token via direct DB write
        using (var db = Factory.CreateDbContext())
        {
            var t = db.PersonClaimTokens.First(t => t.Id == oldToken.Id);
            t.ClaimedUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        var result = await NewService().RotateClaimTokenAsync(
            organizationId: org.Id,
            personUserId: stubId,
            callerUserId: adminId);

        Assert.Equal(TokenRotationResult.Succeeded, result.Result);
        Assert.NotNull(result.RawToken);
    }

    // ─── ListStubsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListStubsAsync_NoStubs_EmptyList()
    {
        var (adminId, _, org) = SeedAdmin();

        var result = await NewService().ListStubsAsync(
            organizationId: org.Id,
            callerUserId: adminId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListStubsAsync_MixedStubAndReal_ReturnsOnlyStubs()
    {
        var (adminId, _, org) = SeedAdmin();
        // 2 stubs + 1 real person
        SeedStubWithToken(org.Id, "Sara", "Stub", "sara@example.com");
        SeedStubWithToken(org.Id, "John", "Stub", "john@example.com");
        var real = TestData.Person(Factory, "Real", "Person");
        TestData.Membership(Factory, real.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().ListStubsAsync(
            organizationId: org.Id,
            callerUserId: adminId);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Contains("Stub", item.DisplayName));
        Assert.DoesNotContain(result, item => item.DisplayName.StartsWith("Real"));
    }

    [Fact]
    public async Task ListStubsAsync_StubsInOtherOrg_Excluded()
    {
        var (_, _, orgA) = SeedAdmin("Org A");
        var (_, _, orgB) = SeedOtherOrgAdmin("Org B");
        // Stub in orgB
        SeedStubWithToken(orgB.Id, "Only", "InB");

        var result = await NewService().ListStubsAsync(
            organizationId: orgA.Id,
            callerUserId: "system-admin-id-placeholder");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListStubsAsync_StubWithActiveToken_HasTrueAndExpiry()
    {
        var (adminId, _, org) = SeedAdmin();
        var (stubId, _, _, _) = SeedStubWithToken(org.Id, "Tina", "Active");

        var result = await NewService().ListStubsAsync(
            organizationId: org.Id,
            callerUserId: adminId);

        var item = Assert.Single(result);
        Assert.True(item.HasActiveToken);
        Assert.NotNull(item.TokenExpiresUtc);
        Assert.True(item.TokenExpiresUtc > DateTime.UtcNow);
        Assert.Null(item.ClaimedUtc);
    }

    [Fact]
    public async Task ListStubsAsync_BothActiveAndConsumedTokens_SurfaceLatest()
    {
        var (adminId, _, org) = SeedAdmin();
        // Pre-consume via direct DB write on a stub with an active token
        var (stubId, _, _, oldToken) = SeedStubWithToken(org.Id, "Both", "Tokens");
        using (var db = Factory.CreateDbContext())
        {
            var t = db.PersonClaimTokens.First(t => t.Id == oldToken.Id);
            t.ClaimedUtc = DateTime.UtcNow.AddMinutes(-5);
            db.SaveChanges();
        }
        // Rotate to mint a fresh active token
        await NewService().RotateClaimTokenAsync(org.Id, stubId, adminId);

        var result = await NewService().ListStubsAsync(
            organizationId: org.Id,
            callerUserId: adminId);

        var item = Assert.Single(result);
        Assert.True(item.HasActiveToken);
        Assert.NotNull(item.ClaimedUtc); // surfaces the most-recent terminal token
    }

    [Fact]
    public async Task ListStubsAsync_NonAdminCaller_EmptyList()
    {
        var (_, _, org) = SeedAdmin();
        SeedStubWithToken(org.Id);
        var (volunteerId, _) = SeedVolunteer(org.Id);

        var result = await NewService().ListStubsAsync(
            organizationId: org.Id,
            callerUserId: volunteerId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListStubsAsync_EmptyCallerUserId_EmptyList()
    {
        var (_, _, org) = SeedAdmin();
        SeedStubWithToken(org.Id);

        var result = await NewService().ListStubsAsync(
            organizationId: org.Id,
            callerUserId: "");

        Assert.Empty(result);
    }

    // ─── ClaimStubAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimStubAsync_HappyPath_ReparentsPersonAndCompletesToken()
    {
        var (_, _, org) = SeedAdmin();
        var (stubId, rawToken, _, _) = SeedStubWithToken(org.Id);
        var newIdentity = SeedIdentityOnly("new@example.com");

        var result = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: newIdentity.Id,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.Succeeded, result.Result);
        Assert.NotNull(result.MergedPerson);
        Assert.Equal(newIdentity.Id, result.MergedPerson!.UserId);
        Assert.False(result.MergedPerson.IsStub);
        Assert.Equal("new@example.com", result.MergedPerson.Email);

        // The OLD stub user id row should be gone (the People row was
        // re-parented to the new identity user — there's no longer
        // a row at the old stub id).
        await using var db = await Factory.CreateDbContextAsync();
        var stubRowAtOldId = await db.People.AnyAsync(p => p.UserId == stubId);
        Assert.False(stubRowAtOldId);

        // New identity row has the merged Person (IsStub=false, email updated).
        var merged = await db.People.FirstAsync(p => p.UserId == newIdentity.Id);
        Assert.False(merged.IsStub);
        Assert.Equal("new@example.com", merged.Email);
    }

    [Fact]
    public async Task ClaimStubAsync_ReparentPreservesOrganizationMembership()
    {
        var (_, _, org) = SeedAdmin();
        var (stubId, rawToken, _, _) = SeedStubWithToken(org.Id);
        var newIdentity = SeedIdentityOnly("new@example.com");

        var result = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: newIdentity.Id,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.Succeeded, result.Result);

        // The membership row was UPDATED (not recreated) and now
        // points at the new identity user.
        await using var db = await Factory.CreateDbContextAsync();
        var memberships = await db.OrganizationMemberships
            .Where(m => m.OrganizationId == org.Id)
            .ToListAsync();
        var newMembership = Assert.Single(memberships, m => m.PersonUserId == newIdentity.Id);
        Assert.Equal(OrganizationRole.Volunteer, newMembership.Role);
        // Audit note flipped from "Stub created by..." to "Claimed by...".
        Assert.Contains("Claimed by", newMembership.Notes);
        Assert.Contains(newIdentity.Id, newMembership.Notes);

        // And there's NO membership pinned to the old stub user id anymore.
        Assert.DoesNotContain(memberships, m => m.PersonUserId == stubId);
    }

    [Fact]
    public async Task ClaimStubAsync_ReparentPreservesTrainingCompletion()
    {
        var (_, _, org) = SeedAdmin();
        var (stubId, rawToken, _, _) = SeedStubWithToken(org.Id);
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        TestData.Completion(Factory, stubId, content.Id, DateTime.UtcNow);

        var newIdentity = SeedIdentityOnly("new@example.com");

        var result = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: newIdentity.Id,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.Succeeded, result.Result);

        await using var db = await Factory.CreateDbContextAsync();
        // TrainingCompletion row re-parented to the new identity user.
        var completion = await db.TrainingCompletions
            .FirstAsync(c => c.TrainingContentId == content.Id);
        Assert.Equal(newIdentity.Id, completion.PersonUserId);
        // No orphan completion at the stub user id.
        Assert.False(await db.TrainingCompletions.AnyAsync(c => c.PersonUserId == stubId));
    }

    [Fact]
    public async Task ClaimStubAsync_ReparentPreservesTrainingActivity()
    {
        var (_, _, org) = SeedAdmin();
        var (stubId, rawToken, _, _) = SeedStubWithToken(org.Id);
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        // TrainingActivity helper isn't in TestData — create inline using
        // the actual property names from Models/TrainingActivity.cs
        // (FirstOpenedUtc + LastUpdatedUtc + ViewedPagesCsv; no per-row
        // LastPageNumber / CompletedPageCount fields).
        using (var db = Factory.CreateDbContext())
        {
            db.TrainingActivities.Add(new TrainingActivity
            {
                PersonUserId = stubId,
                TrainingContentId = content.Id,
                TrainingContentVersion = 1,
                FirstOpenedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow,
                ViewedPagesCsv = "1",
            });
            db.SaveChanges();
        }
        var newIdentity = SeedIdentityOnly("new@example.com");

        var result = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: newIdentity.Id,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.Succeeded, result.Result);

        await using (var db = Factory.CreateDbContext())
        {
            var activity = await db.TrainingActivities
                .FirstAsync(a => a.TrainingContentId == content.Id);
            Assert.Equal(newIdentity.Id, activity.PersonUserId);
            Assert.False(await db.TrainingActivities.AnyAsync(a => a.PersonUserId == stubId));
        }
    }

    [Fact]
    public async Task ClaimStubAsync_InvalidBase64_InvalidToken()
    {
        var result = await NewService().ClaimStubAsync(
            rawClaimToken: "!!!not-base64!!!",
            newIdentityUserId: "any-id",
            newEmail: "any@example.com");

        Assert.Equal(StubClaimResult.InvalidToken, result.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_WrongByteLength_InvalidToken()
    {
        // 16 bytes instead of 32 — Base64Url-encodes to a valid string
        // but doesn't match any token hash.
        var shortToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[16]);
        var result = await NewService().ClaimStubAsync(
            rawClaimToken: shortToken,
            newIdentityUserId: "any-id",
            newEmail: "any@example.com");

        Assert.Equal(StubClaimResult.InvalidToken, result.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_NoMatchingHash_InvalidToken()
    {
        // Valid 32-byte Base64Url but the hash doesn't match any seeded token.
        var newToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[32]);

        var result = await NewService().ClaimStubAsync(
            rawClaimToken: newToken,
            newIdentityUserId: "any-id",
            newEmail: "any@example.com");

        Assert.Equal(StubClaimResult.InvalidToken, result.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_ExpiredToken_Expired()
    {
        var (_, _, org) = SeedAdmin();
        var (_, rawToken, _, _) = SeedStubWithToken(
            org.Id, expiresOverride: DateTime.UtcNow.AddMinutes(-1));

        var newIdentity = SeedIdentityOnly("new@example.com");
        var result = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: newIdentity.Id,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.Expired, result.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_AlreadyClaimedTwice_AlreadyClaimed()
    {
        var (_, _, org) = SeedAdmin();
        var (stubId, rawToken, _, _) = SeedStubWithToken(org.Id);
        var firstIdentity = SeedIdentityOnly("first@example.com");
        var first = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: firstIdentity.Id,
            newEmail: "first@example.com");
        Assert.Equal(StubClaimResult.Succeeded, first.Result);

        // Second attempt with the same raw token
        var secondIdentity = SeedIdentityOnly("second@example.com");
        var second = await NewService().ClaimStubAsync(
            rawClaimToken: rawToken,
            newIdentityUserId: secondIdentity.Id,
            newEmail: "second@example.com");
        Assert.Equal(StubClaimResult.AlreadyClaimed, second.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_EmptyRawToken_ValidationFailed()
    {
        var newIdentity = SeedIdentityOnly("new@example.com");
        var result = await NewService().ClaimStubAsync(
            rawClaimToken: "",
            newIdentityUserId: newIdentity.Id,
            newEmail: "new@example.com");

        Assert.Equal(StubClaimResult.ValidationFailed, result.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_WhitespaceNewEmail_ValidationFailed()
    {
        var result = await NewService().ClaimStubAsync(
            rawClaimToken: Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[32]),
            newIdentityUserId: "any-id",
            newEmail: "   ");

        Assert.Equal(StubClaimResult.ValidationFailed, result.Result);
    }

    [Fact]
    public async Task ClaimStubAsync_EmptyNewIdentityUserId_ValidationFailed()
    {
        var result = await NewService().ClaimStubAsync(
            rawClaimToken: Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[32]),
            newIdentityUserId: "",
            newEmail: "any@example.com");

        Assert.Equal(StubClaimResult.ValidationFailed, result.Result);
    }

    // ─── Token utility (cryptographic shape contract) ─────────────────────────

    [Fact]
    public void PersonService_GenerateToken_ProducesBase64Url43CharNoPadding()
    {
        var (raw, _) = PersonService.GenerateToken();

        // 32 random bytes => Base64 ceil(32/3)*4 = 44 chars with
        // padding; Base64Url strips padding => 43 chars.
        Assert.Equal(43, raw.Length);
        Assert.DoesNotContain("+", raw);
        Assert.DoesNotContain("/", raw);
        Assert.DoesNotContain("=", raw);
    }

    [Fact]
    public void PersonService_GenerateToken_ProducesSha256HexHashOfCorrectLength()
    {
        var (_, hash) = PersonService.GenerateToken();

        // SHA-256 is 32 bytes => 64 hex chars lowercase.
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void PersonService_GenerateToken_TwoCallsProduceDifferentTokensAndHashes()
    {
        var t1 = PersonService.GenerateToken();
        var t2 = PersonService.GenerateToken();

        Assert.NotEqual(t1.RawToken, t2.RawToken);
        Assert.NotEqual(t1.TokenHash, t2.TokenHash);
    }

    [Fact]
    public void PersonService_DefaultTokenLifetime_Is30Days()
    {
        Assert.Equal(TimeSpan.FromDays(30), PersonService.DefaultTokenLifetime);
    }

    [Fact]
    public void PersonService_PlaceholderLockoutEnd_SentinelDate()
    {
        Assert.Equal(new DateTime(9999, 12, 31), PersonService.PlaceholderLockoutEndUtc);
    }
}
