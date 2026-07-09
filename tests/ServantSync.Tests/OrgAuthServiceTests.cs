using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Direct service-level tests for <see cref="OrgAuthService"/>. Distinct from
/// <see cref="PageAccessTests"/> which exercises the gates via the per-service
/// wrappers; this file pins the OrgAuth semantics themselves so a future
/// refactor that flips a comparison silently can't break everything downstream.
///
/// <para>
/// Why this matters: <see cref="OrgAuthService.IsAnyOrgManagerAsync"/> drives
/// <c>NavMenu.razor</c>'s manager-only block (showing Organizations, People,
/// Leagues, Dashboard links). A regression here silently hides admin pages
/// from any user with a Coordinator-only role, or shows them to volunteer-only
/// users. Lock these down before that can happen.
/// </para>
/// </summary>
public class OrgAuthServiceTests : SqliteTestBase
{
    private OrgAuthService NewSvc() => new(Factory);

    // ─── IsAnyOrgManagerAsync (Admin OR Coordinator of any org) ──────────

    [Fact]
    public async Task IsAnyOrgManagerAsync_AdminOfAnyOrg_True()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.True(await NewSvc().IsAnyOrgManagerAsync(admin.UserId));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_CoordinatorOfAnyOrg_True()
    {
        // Coordinator role is intentionally considered "manager" by the
        // nav-menu check (the user requirement was that the manager
        // surfaces are visible to anyone with Admin OR Coordinator).
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsAnyOrgManagerAsync(coordinator.UserId));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_OnlyVolunteerMemberships_False()
    {
        // Caller is only a Volunteer. NavMenu should NOT show the
        // manager-only block (no Organizations / People / Leagues /
        // Dashboard links). A regression that treats Volunteer == manager
        // would expose admin-only affordances.
        var org = TestData.Org(Factory);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().IsAnyOrgManagerAsync(volunteer.UserId));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_NoMembershipsAtAll_False()
    {
        // Caller exists in the system but has no OrganizationMembership rows.
        // We expect false (correctly returning a no-manager signal) instead
        // of throwing or treating the empty-set as a match.
        var nobody = TestData.Person(Factory);

        Assert.False(await NewSvc().IsAnyOrgManagerAsync(nobody.UserId));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_EmptyUserId_False()
    {
        // Empty string is the sentinel NavMenu uses when there's no signed-
        // in user yet. Default false (rather than throwing) so OnInitialized
        // doesn't crash on first SSR frame before Auth populates.
        Assert.False(await NewSvc().IsAnyOrgManagerAsync(""));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_NullUserId_False()
    {
        // The interface signature allows string? to flow through. Defensive
        // false on null rather than a NullReferenceException out of the
        // database.
        Assert.False(await NewSvc().IsAnyOrgManagerAsync(null!));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_MixedRolesPicksManager_True()
    {
        // A user holding Coordinator in Org A and Volunteer in Org B should
        // be a manager (transitively true).
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.Volunteer);

        Assert.True(await NewSvc().IsAnyOrgManagerAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnyOrgAdminAsync_OnlyCoordinator_False()
    {
        // Companion to the above: Coordinator is NOT Admin. A regression
        // that broadens IsAnyOrgAdminAsync to include Coordinator would
        // expose org-create / org-edit affordances to folks who shouldn't
        // have them.
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.False(await NewSvc().IsAnyOrgAdminAsync(coordinator.UserId));
    }

    [Fact]
    public async Task IsAnyOrgManagerAsync_NoOrgAdminRowTrustsCoordinatorRow()
    {
        // Specifically: the underlying query only returns true if AT LEAST
        // ONE of the user's memberships matches Admin OR Coordinator. A
        // query that ANDs roles (mismatched intent like
        // "user is Admin AND Coordinator") would erroneously return false
        // for perfectly valid manager callers.
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.MinistryDirector);

        await using var db = await Factory.CreateDbContextAsync();
        // Sanity: confirm there is no Admin row for this user (only one
        // Coordinator). Then the manager check should still be true.
        Assert.False(await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == coordinator.UserId && m.Role == OrganizationRole.Admin));
        Assert.True(await NewSvc().IsAnyOrgManagerAsync(coordinator.UserId));
    }

    // ─── CanManageSlotAsync (round V per-slot coordinator) ───────────────
    //
    // Per-slot coordinator is the round-V feature added so that orgs can
    // delegate one volunteer opportunity (e.g. "Sound Tech on Sunday") to
    // a specific member without giving them an org-wide role. The full
    // resolution chain is:
    //     slot.CoordinatorPersonUserId == caller
    //     → OR CanManageMinistryAsync(caller, slot.MinistryId)
    // which itself falls back to org Admin/Coordinator or the slot's
    // parent-ministry coordinator. Tests below pin each branch of the
    // chain so a future "simplification" can't accidentally drop a tier.

    [Fact]
    public async Task CanManageSlotAsync_SlotCoordinator_True()
    {
        // Caller IS the slot's own CoordinatorPersonUserId — cheapest path,
        // short-circuits before the ministry chain runs.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Volunteer);
        // Mark them as the slot's coordinator directly via the DbContext
        // (Slot helper doesn't take a coordinator arg yet).
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var s = await db.ServiceSlots.FindAsync(slot.Id);
            s!.CoordinatorPersonUserId = coordinator.UserId;
            await db.SaveChangesAsync();
        }

        Assert.True(await NewSvc().CanManageSlotAsync(coordinator.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_OrgAdmin_True()
    {
        // Caller has no slot-tier delegation but is an Admin of the
        // parent org. Ministry-tier can-manage inherits the org Admin
        // check, so it should still return true.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.True(await NewSvc().CanManageSlotAsync(admin.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_MinistryDirectorOfThisOrg_False()
    {
        // Round-FR-5: a Ministry Director at the org level (no entity-level
        // delegation set on the slot or its ministry) cannot manage the
        // slot. The spec narrowed CanManageOrgAsync to Admin-only, so the
        // org-tier role is only a UI-visibility flag now — actual slot
        // management requires either Admin || slot.CoordinatorPersonUserId
        // || ministry.CoordinatorPersonUserId (covered by the other tests).
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.False(await NewSvc().CanManageSlotAsync(coordinator.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_MinistryCoordinator_True()
    {
        // Direct delegation to the ministry (not org) still wins — the
        // slot falls under that ministry's chain.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var minCoord = TestData.Person(Factory);
        TestData.Membership(Factory, minCoord.UserId, org.Id, OrganizationRole.Volunteer);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var m = await db.Ministries.FindAsync(min.Id);
            m!.CoordinatorPersonUserId = minCoord.UserId;
            await db.SaveChangesAsync();
        }

        Assert.True(await NewSvc().CanManageSlotAsync(minCoord.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_UnrelatedVolunteer_False()
    {
        // Caller has a Volunteer membership in the org but is NOT the
        // slot coordinator, ministry coordinator, or org Admin/Coordinator.
        // The "Volunteer-yet-in-the-org" case is the canonical false
        // answer: being a member is not the same as being able to manage.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().CanManageSlotAsync(volunteer.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_NotInOrgAtAll_False()
    {
        // Caller has no OrganizationMembership row at all. The empty-set
        // branch must return false rather than throw or pass through.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var stranger = TestData.Person(Factory);

        Assert.False(await NewSvc().CanManageSlotAsync(stranger.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_WrongOrgMembership_False()
    {
        // Caller is an Admin of Org B, but slot lives in Org A. Org
        // membership is per-organization, so an Admin-of-Org-B cannot
        // manage resources in Org A just because of their cross-org
        // Admin role.
        var orgA = TestData.Org(Factory, "Org A");
        var min = TestData.Ministry(Factory, orgA.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var orgBAdmin = TestData.Person(Factory);
        var orgB = TestData.Org(Factory, "Org B");
        TestData.Membership(Factory, orgBAdmin.UserId, orgB.Id, OrganizationRole.Admin);

        Assert.False(await NewSvc().CanManageSlotAsync(orgBAdmin.UserId, slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_EmptyUserId_False()
    {
        // Plumb the empty-userId sentinel that Blazor pages pass before
        // auth state resolves. Default false rather than throw so the
        // OnInitialized gate doesn't crash on first SSR frame.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);

        Assert.False(await NewSvc().CanManageSlotAsync("", slot.Id));
    }

    [Fact]
    public async Task CanManageSlotAsync_NonexistentSlot_False()
    {
        // A slot id that doesn't exist should return false (not throw).
        // A regression that swallowed the "slot is null" early-return
        // would silently treat non-existent slots as manageable.
        var caller = TestData.Person(Factory);
        Assert.False(await NewSvc().CanManageSlotAsync(caller.UserId, slotId: 99_999));
    }

    // ─── CanManageOrgAsync (per-org Admin OR Coordinator) ──────────────
    // Per-org counterpart to IsAnyOrgManagerAsync. Drives the
    // Organizations/Detail.razor Org-training tab gate: a Coordinator
    // of THIS org should be able to manage org-wide training, even
    // though they can't add members. Pin the per-org semantics so a
    // future refactor that conflates "any org" with "this org" can't
    // silently open the gate to foreign-org Coordinators.

    [Fact]
    public async Task CanManageOrgAsync_AdminOfThisOrg_True()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.True(await NewSvc().CanManageOrgAsync(admin.UserId, org.Id));
    }

    [Fact]
    public async Task CanManageOrgAsync_MinistryDirectorOfThisOrg_False()
    {
        // Round-FR-5: CanManageOrgAsync narrowed to Admin-only. A
        // Ministry Director at the org level is no longer a per-org
        // manager — the role only drives UI visibility (dashboard
        // + training-catalog nav links) and the entity-level
        // CoordinatorPersonUserId FK drives actual ministry/slot
        // management. This test pins the new deny path: a Ministry
        // Director with no Admin role must NOT pass an org-wide gate.
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.False(await NewSvc().CanManageOrgAsync(coordinator.UserId, org.Id));
    }

    [Fact]
    public async Task CanManageOrgAsync_OnlyVolunteerOfThisOrg_False()
    {
        // The user's "I can make training mandatory" bug: a Volunteer
        // member of the org must NOT be able to manage org training.
        var org = TestData.Org(Factory);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().CanManageOrgAsync(volunteer.UserId, org.Id));
    }

    [Fact]
    public async Task CanManageOrgAsync_AdminOfDifferentOrg_False()
    {
        // Admin of Org B should NOT pass the per-org check for Org A.
        // A regression that ignores the orgId parameter would expose
        // org training to foreign-org Admins.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var orgBAdmin = TestData.Person(Factory);
        TestData.Membership(Factory, orgBAdmin.UserId, orgB.Id, OrganizationRole.Admin);

        Assert.False(await NewSvc().CanManageOrgAsync(orgBAdmin.UserId, orgA.Id));
    }

    [Fact]
    public async Task CanManageOrgAsync_NotAMemberAtAll_False()
    {
        // Empty-set branch: caller exists in the system but has no
        // OrganizationMembership rows. Should return false (not throw).
        var org = TestData.Org(Factory);
        var stranger = TestData.Person(Factory);

        Assert.False(await NewSvc().CanManageOrgAsync(stranger.UserId, org.Id));
    }

    [Fact]
    public async Task CanManageOrgAsync_EmptyUserId_False()
    {
        // Plumb the empty-userId sentinel that Blazor pages pass before
        // auth state resolves. Default false rather than throw so the
        // OnInitialized gate doesn't crash on first SSR frame.
        var org = TestData.Org(Factory);
        Assert.False(await NewSvc().CanManageOrgAsync("", org.Id));
    }

    // ─── IsSystemAdminAsync (ASP.NET Core Identity role check) ─────────
    // SystemAdmin is the strictly-visibility-only role added in the
    // round-AW RBAC refactor. It lets the caller see every org/person/
    // assignment but does NOT widen per-org ADMIN write gates — the
    // IsOrgAdminAsync / CanManageOrgAsync / CanManageSlotAsync / etc.
    // checks above are unchanged. The single per-org-write
    // intentional widening is in OrganizationService.CreateOrgAsync
    // (org-create is god-mode for SystemAdmin because the user
    // requirement was "only the systemadmin can add organizations").
    //
    // The tests below seed the IdentityRole + IdentityUserRole rows
    // directly via the EF context (AddToRoleAsync would require a
    // full UserManager<IdentityUser> DI registration in the test
    // fixture, which is heavier than the schema-level insert that
    // exercises the same code path IsSystemAdminAsync reads).

    [Fact]
    public async Task IsSystemAdminAsync_AssignedIdentityRole_True()
    {
        var admin = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, admin.UserId);

        Assert.True(await NewSvc().IsSystemAdminAsync(admin.UserId));
    }

    [Fact]
    public async Task IsSystemAdminAsync_NotInRole_False()
    {
        // Caller exists in Identity but has no UserRoles row pointing
        // at the SystemAdmin IdentityRole. Even though they're an
        // OrganizationRole.Admin they should NOT pass this check — the
        // API is STRICTLY the identity role, not the per-org RBAC tier.
        var org = TestData.Org(Factory);
        var orgAdmin = TestData.Person(Factory);
        TestData.Membership(Factory, orgAdmin.UserId, org.Id, OrganizationRole.Admin);
        await TestData.SeedSystemAdminRoleAsync(Factory, /* leave this user out */ "other-user-id");

        Assert.False(await NewSvc().IsSystemAdminAsync(orgAdmin.UserId));
    }

    [Fact]
    public async Task IsSystemAdminAsync_EmptyUserId_False()
    {
        // Same sentinel pattern as the other gate tests — anonymous /
        // not-yet-resolved auth state should not throw.
        await TestData.SeedSystemAdminRoleAsync(Factory, /* leave caller empty by skipping user row */ "");
        Assert.False(await NewSvc().IsSystemAdminAsync(""));
    }

    [Fact]
    public async Task IsSystemAdminAsync_RoleNotInSchema_False()
    {
        // Sanity: when the IdentityRoles table has no SystemAdmin row
        // at all (e.g. a freshly-created DB where seeder hasn't yet
        // run), the API returns false rather than throwing. The
        // cached role id stays empty; subsequent calls re-check the
        // schema on the off-chance the seeder runs mid-app-lifetime.
        var nobody = TestData.Person(Factory);
        // Don't seed SystemAdmin role this time.
        Assert.False(await NewSvc().IsSystemAdminAsync(nobody.UserId));
    }

    [Fact]
    public async Task IsSystemAdminAsync_DoesNotWidenPerOrgAdminGate()
    {
        // The whole point of the "strict visibility-only" decision:
        // a SystemAdmin who is NOT OrganizationRole.Admin of org A
        // must NOT suddenly pass CanManageOrgAsync for org A. We
        // verify the inverse: assigning SystemAdmin alone is NOT
        // enough to make an org-writable caller. This pins "the two
        // tiers remain orthogonal".
        var org = TestData.Org(Factory);
        var sysOnly = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, sysOnly.UserId);
        Assert.False(await NewSvc().CanManageOrgAsync(sysOnly.UserId, org.Id));
        Assert.False(await NewSvc().IsOrgAdminAsync(sysOnly.UserId, org.Id));
    }

    // The actionable helper used to live here as a private method; it
    // moved to TestData.SeedSystemAdminRoleAsync(Factory, userId) so
    // OrganizationServiceTests could reuse the same FK-safe seed path
    // without copy-pasting the IdentityRole + IdentityUser + UserRoles
    // trio. Tests above call TestData.SeedSystemAdminRoleAsync(Factory, …).
}
