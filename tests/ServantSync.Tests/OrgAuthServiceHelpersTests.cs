using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Round-FR-5 coverage for the 5 new role-flag helpers added to
/// <see cref="IOrgAuthService"/> that drive the role-aware NavMenu
/// + Dashboard scope:
///   * <see cref="IOrgAuthService.IsMinistryDirectorAsync"/> (single-org)
///   * <see cref="IOrgAuthService.IsSlotCoordinatorAsync"/>  (single-org)
///   * <see cref="IOrgAuthService.IsAnyMinistryDirectorAsync"/> (any-org)
///   * <see cref="IOrgAuthService.IsAnySlotCoordinatorAsync"/>  (any-org)
///   * <see cref="IOrgAuthService.IsAnyTrainingManagerAsync"/>  (any-org,
///     Admin || MinistryDirector)
///
/// The existing CanManage*Async tests cover the spec's "negative side"
/// of org/ministry/slot management. This file pins the per-role
/// flag helpers that the UI consults to decide which nav links and
/// which Dashboard query path a user sees.
///
/// Convention: each helper gets one positive row + one
/// "wrong-role-not-an-exact-match" row + one "not a member" row at
/// minimum; the any-org helpers additionally test multi-org + the
/// cross-role-co-existing rows that are realistic for real users.
/// </summary>
public class OrgAuthServiceHelpersTests : SqliteTestBase
{
    private OrgAuthService NewSvc() => new(Factory);

    // ─── IsMinistryDirectorAsync: single-org role-flag ───────────────────

    [Fact]
    public async Task IsMinistryDirectorAsync_RoleMatches_True()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsMinistryDirectorAsync(user.UserId, org.Id));
    }

    [Fact]
    public async Task IsMinistryDirectorAsync_AdminNotExactMatch_False()
    {
        // Per the interface doc, this is an EXACT-ROLE match — not the
        // "covers Admin too" semantics of CanManageOrgAsync. Admin has
        // its dedicated IsOrgAdminAsync / IsAnyOrgAdminAsync helpers.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.False(await NewSvc().IsMinistryDirectorAsync(admin.UserId, org.Id));
    }

    [Fact]
    public async Task IsMinistryDirectorAsync_SlotCoordinatorNotExactMatch_False()
    {
        var org = TestData.Org(Factory);
        var slotCoord = TestData.Person(Factory);
        TestData.Membership(Factory, slotCoord.UserId, org.Id, OrganizationRole.SlotCoordinator);

        Assert.False(await NewSvc().IsMinistryDirectorAsync(slotCoord.UserId, org.Id));
    }

    [Fact]
    public async Task IsMinistryDirectorAsync_Volunteer_False()
    {
        var org = TestData.Org(Factory);
        var vol = TestData.Person(Factory);
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().IsMinistryDirectorAsync(vol.UserId, org.Id));
    }

    [Fact]
    public async Task IsMinistryDirectorAsync_NotAMember_False()
    {
        var org = TestData.Org(Factory);
        var someone = TestData.Person(Factory);
        // no Membership inserted

        Assert.False(await NewSvc().IsMinistryDirectorAsync(someone.UserId, org.Id));
    }

    [Fact]
    public async Task IsMinistryDirectorAsync_DifferentOrgMembership_False()
    {
        // User IS a Ministry Director in OrgB but NOT a member of OrgA.
        // Single-org helper must scope strictly to orgA membership.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.MinistryDirector);

        Assert.False(await NewSvc().IsMinistryDirectorAsync(user.UserId, orgA.Id));
    }

    [Fact]
    public async Task IsMinistryDirectorAsync_EmptyUserId_False()
    {
        // GetRoleAsync returns null for empty userId; the helper must
        // coerce that to a clean False rather than throwing.
        var org = TestData.Org(Factory);

        Assert.False(await NewSvc().IsMinistryDirectorAsync("", org.Id));
    }

    // ─── IsSlotCoordinatorAsync: single-org role-flag ────────────────────

    [Fact]
    public async Task IsSlotCoordinatorAsync_RoleMatches_True()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.SlotCoordinator);

        Assert.True(await NewSvc().IsSlotCoordinatorAsync(user.UserId, org.Id));
    }

    [Fact]
    public async Task IsSlotCoordinatorAsync_AdminNotExactMatch_False()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.False(await NewSvc().IsSlotCoordinatorAsync(admin.UserId, org.Id));
    }

    [Fact]
    public async Task IsSlotCoordinatorAsync_MinistryDirectorNotExactMatch_False()
    {
        var org = TestData.Org(Factory);
        var md = TestData.Person(Factory);
        TestData.Membership(Factory, md.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.False(await NewSvc().IsSlotCoordinatorAsync(md.UserId, org.Id));
    }

    [Fact]
    public async Task IsSlotCoordinatorAsync_Volunteer_False()
    {
        var org = TestData.Org(Factory);
        var vol = TestData.Person(Factory);
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().IsSlotCoordinatorAsync(vol.UserId, org.Id));
    }

    [Fact]
    public async Task IsSlotCoordinatorAsync_NotAMember_False()
    {
        var org = TestData.Org(Factory);
        var someone = TestData.Person(Factory);

        Assert.False(await NewSvc().IsSlotCoordinatorAsync(someone.UserId, org.Id));
    }

    [Fact]
    public async Task IsSlotCoordinatorAsync_DifferentOrgMembership_False()
    {
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.SlotCoordinator);

        Assert.False(await NewSvc().IsSlotCoordinatorAsync(user.UserId, orgA.Id));
    }

    [Fact]
    public async Task IsSlotCoordinatorAsync_EmptyUserId_False()
    {
        var org = TestData.Org(Factory);

        Assert.False(await NewSvc().IsSlotCoordinatorAsync("", org.Id));
    }

    // ─── IsAnyMinistryDirectorAsync: any-org role-flag ───────────────────

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_RoleMatchesInOneOrg_True()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsAnyMinistryDirectorAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_RoleMatchesAcrossMultipleOrgs_True()
    {
        // Multi-org: MD in OrgA AND MD in OrgB — pins that we don't
        // accidentally require strict single-org membership. Realistic
        // for pastors who oversee multiple ministries across churches.
        var orgA = TestData.Org(Factory, "A");
        var orgB = TestData.Org(Factory, "B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsAnyMinistryDirectorAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_AdminAnywhere_False()
    {
        // Admin does NOT count for the MD nav flag — the role-flag
        // helpers are exact-match, only the OR of the 3 helper types
        // (Admin / MD / SC) drives IsAnyOrgManagerAsync differently.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.False(await NewSvc().IsAnyMinistryDirectorAsync(admin.UserId));
    }

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_SlotCoordinator_False()
    {
        var org = TestData.Org(Factory);
        var slotCoord = TestData.Person(Factory);
        TestData.Membership(Factory, slotCoord.UserId, org.Id, OrganizationRole.SlotCoordinator);

        Assert.False(await NewSvc().IsAnyMinistryDirectorAsync(slotCoord.UserId));
    }

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_VolunteerOnly_False()
    {
        var org = TestData.Org(Factory);
        var vol = TestData.Person(Factory);
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().IsAnyMinistryDirectorAsync(vol.UserId));
    }

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_NotAMemberAnywhere_False()
    {
        var someone = TestData.Person(Factory);
        // no Memberships inserted anywhere

        Assert.False(await NewSvc().IsAnyMinistryDirectorAsync(someone.UserId));
    }

    [Fact]
    public async Task IsAnyMinistryDirectorAsync_EmptyUserId_False()
    {
        Assert.False(await NewSvc().IsAnyMinistryDirectorAsync(""));
    }

    // ─── IsAnySlotCoordinatorAsync: any-org role-flag ────────────────────

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_RoleMatchesInOneOrg_True()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.SlotCoordinator);

        Assert.True(await NewSvc().IsAnySlotCoordinatorAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_RoleMatchesAcrossMultipleOrgs_True()
    {
        // Multi-org: SC in OrgA AND SC in OrgB — realistic for a
        // per-slot lead who runs a Welcome Desk in two different
        // churches. The any-org flag should still surface the
        // dashboard link.
        var orgA = TestData.Org(Factory, "A");
        var orgB = TestData.Org(Factory, "B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.SlotCoordinator);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.SlotCoordinator);

        Assert.True(await NewSvc().IsAnySlotCoordinatorAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_AdminAnywhere_False()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.False(await NewSvc().IsAnySlotCoordinatorAsync(admin.UserId));
    }

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_MinistryDirectorAnywhere_False()
    {
        var org = TestData.Org(Factory);
        var md = TestData.Person(Factory);
        TestData.Membership(Factory, md.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.False(await NewSvc().IsAnySlotCoordinatorAsync(md.UserId));
    }

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_VolunteerOnly_False()
    {
        var org = TestData.Org(Factory);
        var vol = TestData.Person(Factory);
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().IsAnySlotCoordinatorAsync(vol.UserId));
    }

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_NotAMemberAnywhere_False()
    {
        var someone = TestData.Person(Factory);

        Assert.False(await NewSvc().IsAnySlotCoordinatorAsync(someone.UserId));
    }

    [Fact]
    public async Task IsAnySlotCoordinatorAsync_EmptyUserId_False()
    {
        Assert.False(await NewSvc().IsAnySlotCoordinatorAsync(""));
    }

    // ─── IsAnyTrainingManagerAsync: Admin||MD at any org ─────────────────

    [Fact]
    public async Task IsAnyTrainingManagerAsync_AdminAnywhere_True()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        Assert.True(await NewSvc().IsAnyTrainingManagerAsync(admin.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_MinistryDirectorAnywhere_True()
    {
        var org = TestData.Org(Factory);
        var md = TestData.Person(Factory);
        TestData.Membership(Factory, md.UserId, org.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsAnyTrainingManagerAsync(md.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_AdminInOneOrgMinistryDirectorInAnother_True()
    {
        // Multi-org mixed: Admin in OrgA AND MinistryDirector in OrgB.
        // Either role suffices — any-org OR semantics.
        var orgA = TestData.Org(Factory, "A");
        var orgB = TestData.Org(Factory, "B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsAnyTrainingManagerAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_MixedWithSlotCoordinatorAlsoPresent_True()
    {
        // User has SlotCoordinator in OrgA AND MinistryDirector in OrgB
        // — still TRUE (the MD role qualifies even though the SC does
        // not). Pins that the SC deny path doesn't bleed over when
        // SC is one of multiple memberships.
        var orgA = TestData.Org(Factory, "A");
        var orgB = TestData.Org(Factory, "B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.SlotCoordinator);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.MinistryDirector);

        Assert.True(await NewSvc().IsAnyTrainingManagerAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_SlotCoordinatorAnywhere_False()
    {
        // Per the interface doc, Slot Coordinators manage slots, not
        // the training catalog. Deliberately NOT included in the
        // training manager OR — staff-readable comment in
        // OrgAuthService.cs cites the matching per-org counterpart used
        // by the Training/Manage page gate.
        var org = TestData.Org(Factory);
        var slotCoord = TestData.Person(Factory);
        TestData.Membership(Factory, slotCoord.UserId, org.Id, OrganizationRole.SlotCoordinator);

        Assert.False(await NewSvc().IsAnyTrainingManagerAsync(slotCoord.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_OnlySlotCoordinatorAcrossAllOrgs_False()
    {
        // User has SlotCoordinator in OrgA AND OrgB but nothing else.
        // Result is False: the spec deliberately excludes SC from
        // training management even when SC is the user's only
        // management-tier role across every org they belong to. This
        // is the most subtle spec line and worth its own pin.
        var orgA = TestData.Org(Factory, "A");
        var orgB = TestData.Org(Factory, "B");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.SlotCoordinator);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.SlotCoordinator);

        Assert.False(await NewSvc().IsAnyTrainingManagerAsync(user.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_VolunteerOnly_False()
    {
        var org = TestData.Org(Factory);
        var vol = TestData.Person(Factory);
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        Assert.False(await NewSvc().IsAnyTrainingManagerAsync(vol.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_NotAMemberAnywhere_False()
    {
        var someone = TestData.Person(Factory);

        Assert.False(await NewSvc().IsAnyTrainingManagerAsync(someone.UserId));
    }

    [Fact]
    public async Task IsAnyTrainingManagerAsync_EmptyUserId_False()
    {
        Assert.False(await NewSvc().IsAnyTrainingManagerAsync(""));
    }
}
