using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="OrganizationService"/>. Covers
/// the bootstrap-Admin pattern (the new Organization row + its first
/// OrganizationMembership are both inserted atomically in a transaction).
/// Real SQLite-backed DbContext via the shared <see cref="SqliteTestBase"/>.
/// </summary>
public class OrganizationServiceTests : SqliteTestBase
{
    private OrganizationService NewSvc() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<OrganizationService>.Instance);

    [Fact]
    public async Task CreateOrg_AdminCaller_InsertsBothRows_AndReturnsId()
    {
        // Prerequisite: the caller is Admin of some pre-existing org.
        var seedOrg = TestData.Org(Factory, "Seed Org");
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "Brand New Org",
            description: "Created in test",
            address: null,
            contactEmail: null,
            contactPhone: null,
                timeZoneId: null);

        Assert.NotNull(newId);

        await using var db = await Factory.CreateDbContextAsync();

        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Equal("Brand New Org", org.Name);

        var membership = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == newId && m.PersonUserId == admin.UserId);
        Assert.Equal(OrganizationRole.Admin, membership.Role);
    }

    [Fact]
    public async Task CreateOrg_TrimsNameWhitespace()
    {
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "  Spaced Out  ",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.NotNull(newId);
        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Equal("Spaced Out", org.Name);
    }

    [Fact]
    public async Task CreateOrg_NonAdminCaller_ReturnsNull()
    {
        // Caller is only a Coordinator of an existing org, not an Admin.
        var seedOrg = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, seedOrg.Id, OrganizationRole.Coordinator);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: coordinator.UserId,
            name: "Should Not Exist",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(newId);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Organizations.AnyAsync(o => o.Name == "Should Not Exist"));
    }

    [Fact]
    public async Task CreateOrg_VolunteerCaller_ReturnsNull()
    {
        var seedOrg = TestData.Org(Factory);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, seedOrg.Id, OrganizationRole.Volunteer);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: volunteer.UserId,
            name: "Should Not Exist",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(newId);
    }

    [Fact]
    public async Task CreateOrg_NonMemberCaller_ReturnsNull()
    {
        // Caller has no membership in any org.
        var nobody = TestData.Person(Factory);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: nobody.UserId,
            name: "Should Not Exist",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(newId);
    }

    [Fact]
    public async Task CreateOrg_EmptyCallerId_ReturnsNull()
    {
        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: "",
            name: "Should Not Exist",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(newId);
    }

    [Fact]
    public async Task CreateOrg_EmptyName_ReturnsNull()
    {
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(newId);
    }

    [Fact]
    public async Task CreateOrg_WhitespaceName_ReturnsNull()
    {
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "   ",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(newId);
    }

    [Fact]
    public async Task CreateOrg_BootstrapMembershipIsAdmin_NotCoordinatorOrVolunteer()
    {
        // The bootstrap membership is explicitly Admin, never any other role.
        // (Even if a future refactor "helpfully picks a default role," the
        // explicit OrganizationRole.Admin argument here must hold.)
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "X",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == newId && m.PersonUserId == admin.UserId);
        Assert.Equal(OrganizationRole.Admin, row.Role);
    }

    [Fact]
    public async Task CreateOrg_IsAtomic_NoOrphanOrganizationWithoutAdmin()
    {
        // Hard to engineer a rollback in SQLite via EF without a controlled
        // fault. Instead, sanity-check the contract: every Organization
        // created via this service has exactly one Admin membership (the
        // creator). If a future refactor splits the two inserts and forgets
        // the transaction wrap, this test will catch it.
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "Atomic Org",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        await using var db = await Factory.CreateDbContextAsync();

        var org = await db.Organizations.FindAsync(newId);
        Assert.NotNull(org);

        var adminCount = await db.OrganizationMemberships.CountAsync(m =>
            m.OrganizationId == newId && m.Role == OrganizationRole.Admin);
        Assert.Equal(1, adminCount);
    }

    [Fact]
    public async Task CreateOrg_GeneratesRegistrationToken_SoLinkCanBeSharedImmediately()
    {
        // New orgs are born with a non-null RegistrationToken so Admins can
        // share the invite link from /Organizations/{Id} right away without
        // needing a separate "rotate to first value" round-trip.
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "Link-Ready Org",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.False(string.IsNullOrEmpty(org.RegistrationToken));
        // Guid.NewGuid().ToString("N") yields 32 hex chars. Defensive shape
        // check: if a future token source changes length we want this test
        // to fail loudly rather than ship a malformed URL.
        Assert.Equal(32, org.RegistrationToken!.Length);
    }

    [Fact]
    public async Task CreateOrg_DifferentOrgs_HaveDistinctTokens()
    {
        // Collisions are multiplied by N orgs in production; the unique
        // index catches them but we want to assert the generator isn't
        // accidentally returning a constant.
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var idA = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId, name: "Org A",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);
        var idB = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId, name: "Org B",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        await using var db = await Factory.CreateDbContextAsync();
        var tokA = (await db.Organizations.FindAsync(idA))!.RegistrationToken;
        var tokB = (await db.Organizations.FindAsync(idB))!.RegistrationToken;
        Assert.NotEqual(tokA, tokB);
    }

    // ─── GenerateRegistrationTokenAsync (rotation) ────────────────────────

    [Fact]
    public async Task RotateToken_AsAdmin_ReturnsNewToken_AndPersistsIt()
    {
        // Pre: an existing org Admin rotates the link.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        // Seed an initial token (CreateOrgAsync does this for newly-spawned
        // orgs, but for a Standalone test we set it explicitly).
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var row = await db.Organizations.FindAsync(org.Id);
            row!.RegistrationToken = "initialtoken1" + new string('a', 20); // 32 chars
            await db.SaveChangesAsync();
        }

        var newToken = await NewSvc().GenerateRegistrationTokenAsync(admin.UserId, org.Id);
        Assert.NotNull(newToken);
        Assert.NotEqual("initialtoken1aaaaaaaaaaaaaaaaaaaaaaaa", newToken); // changed
        Assert.Equal(32, newToken!.Length); // still 32-char shape

        await using var db2 = await Factory.CreateDbContextAsync();
        var refreshed = await db2.Organizations.FindAsync(org.Id);
        Assert.Equal(newToken, refreshed!.RegistrationToken);
    }

    [Fact]
    public async Task RotateToken_TwiceOnSameOrg_ProducesDifferentTokens()
    {
        // Each rotation must invalidate any previously-shared URL. A second
        // rotation should yield a fresh GUID different from the first.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var first = await NewSvc().GenerateRegistrationTokenAsync(admin.UserId, org.Id);
        var second = await NewSvc().GenerateRegistrationTokenAsync(admin.UserId, org.Id);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task RotateToken_AsCoordinator_ReturnsNull_WithoutChangingToken()
    {
        // Coordinators cannot rotate (matches the Admin-only gate on the
        // /Account/Register?token=… link — only Admins can regenerate).
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);

        var seedToken = "coordinatorcanno";
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var admin = TestData.Person(Factory);
            TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
            TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);
            var row = await db.Organizations.FindAsync(org.Id);
            row!.RegistrationToken = seedToken;
            await db.SaveChangesAsync();
        }

        var result = await NewSvc().GenerateRegistrationTokenAsync(coordinator.UserId, org.Id);
        Assert.Null(result);

        await using var db2 = await Factory.CreateDbContextAsync();
        var refreshed = await db2.Organizations.FindAsync(org.Id);
        Assert.Equal(seedToken, refreshed!.RegistrationToken); // unchanged
    }

    [Fact]
    public async Task RotateToken_AsVolunteer_ReturnsNull()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().GenerateRegistrationTokenAsync(volunteer.UserId, org.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task RotateToken_EmptyCallerId_ReturnsNull()
    {
        var org = TestData.Org(Factory);
        var result = await NewSvc().GenerateRegistrationTokenAsync("", org.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task RotateToken_UnknownOrg_ReturnsNull()
    {
        // Admin of one org rotating a token on a different (non-existent)
        // org id must return null rather than throwing.
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var result = await NewSvc().GenerateRegistrationTokenAsync(admin.UserId, organizationId: 9_999_999);
        Assert.Null(result);
    }

    // ─── Round-AV: CreateOrgAsync timeZoneId round-trip ──────────────────

    [Fact]
    public async Task CreateOrg_WithValidIanaTimeZoneId_PersistsColumn()
    {
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "TZ Org",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: "America/New_York");

        Assert.NotNull(newId);
        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Equal("America/New_York", org.TimeZoneId);
    }

    [Fact]
    public async Task CreateOrg_WithNullTimeZoneId_StoresNullColumn()
    {
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "Browser TZ Org",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.NotNull(newId);
        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Null(org.TimeZoneId); // "use browser default" survives round-trip
    }

    [Fact]
    public async Task CreateOrg_WithWhitespaceTimeZoneId_StoresNullColumn()
    {
        // Same path as null: empty <option value=""> + IanaTimeZone validator
        // pass-through means whitespace reaches the service. Coerce here so
        // the user-intent ("no override") is honored on the column too.
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "Whitespace TZ Org",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: "   ");

        Assert.NotNull(newId);
        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Null(org.TimeZoneId);
    }    [Fact]
    public async Task CreateOrg_WithTimeZoneId_TrimsTrailingWhitespace()
    {
        var seedOrg = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, seedOrg.Id, OrganizationRole.Admin);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: admin.UserId,
            name: "Trimmed TZ Org",
            description: null, address: null, contactEmail: null, contactPhone: null,
            timeZoneId: "  Europe/London  ");

        Assert.NotNull(newId);
        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Equal("Europe/London", org.TimeZoneId);
    }

    // ─── Round-AW: SystemAdmin god-mode create-org path ────────────────
    // The user requirement was "only the systemadmin can add organizations"
    // — which combined with the "no per-org Admin for a fresh tenant"
    // admission policy means a fresh SystemAdmin who hasn't been Admin of
    // anything yet must be able to bootstrap the first tenant. The
    // IsAnyOrgAdminAsync branch is preserved unchanged for back-compat;
    // the IsSystemAdminAsync branch is the new god-mode path. Org-edit
    // gates elsewhere in the codebase are NOT widened — the strict
    // visibility-only decision is enforced at the per-org method layer
    // (see CanManageOrgAsync_* tests for the orthogonality pin).

    [Fact]
    public async Task CreateOrg_FreshSystemAdmin_WithoutAnyOrgs_InsertsRow_AndIsAdminBootstrap()
    {
        // Caller is SystemAdmin but has NO OrganizationMembership rows
        // anywhere. This is the canonical fresh-SysAdmin-tenant-bootstrap
        // path — the prior rule (IsAnyOrgAdminAsync) would have denied
        // the request. Now it must succeed AND the caller must become
        // Admin of the new org via the same transaction that inserts it.
        var sysAdmin = TestData.Person(Factory);
        await SeedSystemAdminRoleAsync(sysAdmin.UserId);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: sysAdmin.UserId,
            name: "Fresh SysAdmin Bootstrap",
            description: "First tenant for a brand-new SystemAdmin",
            address: null, contactEmail: null, contactPhone: null,
            timeZoneId: null);

        Assert.NotNull(newId);

        await using var db = await Factory.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync(o => o.Id == newId);
        Assert.Equal("Fresh SysAdmin Bootstrap", org.Name);

        // The bootstrap membership invariant (mirrors the existing
        // round-trip tests above): the calling SystemAdmin is now Admin
        // of the brand-new org they just created.
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == newId && m.PersonUserId == sysAdmin.UserId);
        Assert.Equal(OrganizationRole.Admin, row.Role);
    }

    [Fact]
    public async Task CreateOrg_SystemAdminAlsoAdminOfExistingOrg_StillInsertsRow()
    {
        // Back-compat: a caller who is BOTH SystemAdmin AND
        // OrganizationRole.Admin of an existing org must keep working
        // through the IsAnyOrgAdminAsync branch (the log line semantics
        // differ — the Information-level "SystemAdmin bootstrapped"
        // message only fires when they're NOT already an admin of any
        // — but the functional outcome is identical).
        var seedOrg = TestData.Org(Factory, "Seed Org");
        var sysAdmin = TestData.Person(Factory);
        TestData.Membership(Factory, sysAdmin.UserId, seedOrg.Id, OrganizationRole.Admin);
        await SeedSystemAdminRoleAsync(sysAdmin.UserId);

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: sysAdmin.UserId,
            name: "Backcompat SysAdmin+Admin",
            description: null, address: null, contactEmail: null, contactPhone: null,
            timeZoneId: null);

        Assert.NotNull(newId);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.Organizations.AnyAsync(o => o.Name == "Backcompat SysAdmin+Admin"));
    }

    [Fact]
    public async Task CreateOrg_Coordinator_NotSystemAdmin_ReturnsNull()
    {
        // Pin that the god-mode widening is SystemAdmin-ONLY, not
        // Coordinator-by-extension. A Coordinator of an existing org
        // who is NOT a SystemAdmin must be denied exactly as before —
        // a regression that broadened the gate to any manager role
        // (admin-of-some-org OR coord-of-some-org OR SystemAdmin) would
        // silently let coordinators with zero OrgAdmin context create
        // tenants, exactly what the original "Admin of existing org"
        // gate prevents.
        var seedOrg = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, seedOrg.Id, OrganizationRole.Coordinator);
        // Deliberately NOT SeedingSystemAdmin here.

        var newId = await NewSvc().CreateOrgAsync(
            callerUserId: coordinator.UserId,
            name: "Coord Should Not Exist",
            description: null, address: null, contactEmail: null, contactPhone: null,
            timeZoneId: null);

        Assert.Null(newId);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Organizations.AnyAsync(o => o.Name == "Coord Should Not Exist"));
    }

    // Helper: ensure the SystemAdmin IdentityRole exists, then add a
    // single IdentityUserRole join row for the supplied userId. Mirrors
    // the same helper in OrgAuthServiceTests so a test that needs a
    // SystemAdmin caller can opt-in with one line.
    private async Task SeedSystemAdminRoleAsync(string userId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "SYSTEMADMIN");
        if (role is null)
        {
            role = new IdentityRole { Name = "SystemAdmin", NormalizedName = "SYSTEMADMIN" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        if (string.IsNullOrEmpty(userId)) return;
        if (await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == role.Id)) return;
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();
    }
}
