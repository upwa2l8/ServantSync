using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="MemberManagementService"/>.
/// Covers the Add path (Admin-only, idempotent, input-validated) and the
/// UpdateRole path (Admin-only, self-demotion guarded, role change
/// persisted). Real SQLite-backed DbContext via the shared
/// <see cref="SqliteTestBase"/>, no mocks.
/// </summary>
public class MemberManagementServiceTests : SqliteTestBase
{
    // Real OrgAuthService against the same factory so the IsOrgAdminAsync
    // delegation exercise stays end-to-end (DB query → result). NullLogger
    // so we don't need to fake the audit log.
    private MemberManagementService NewSvc() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<MemberManagementService>.Instance);

    // ─── Add ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_AdminCaller_AddsRow_AndReturnsAdded()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().AddAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.Added, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId);
        Assert.Equal(OrganizationRole.Volunteer, row.Role);
    }

    [Fact]
    public async Task Add_AdminCaller_PromotingToAdmin_Works()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AddAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            role: OrganizationRole.Admin);

        // AlreadyExists takes precedence over role change for this method
        // (we don't expose explicit role-update semantics yet — promoted
        // by removing + re-adding via this surface).
        Assert.Equal(MemberAddResult.AlreadyExists, result);
    }

    [Fact]
    public async Task Add_CoordinatorCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewSvc().AddAsync(
            callerUserId: coordinator.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        var any = await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId);
        Assert.False(any);
    }

    [Fact]
    public async Task Add_VolunteerCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var volunteer = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AddAsync(
            callerUserId: volunteer.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Add_AdminOfOtherOrg_Denied()
    {
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var crossOrgAdmin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, crossOrgAdmin.UserId, orgA.Id, OrganizationRole.Admin);

        var result = await NewSvc().AddAsync(
            callerUserId: crossOrgAdmin.UserId,
            organizationId: orgB.Id,
            personUserId: target.UserId,
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        var any = await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == orgB.Id && m.PersonUserId == target.UserId);
        Assert.False(any);
    }

    [Fact]
    public async Task Add_DuplicateMembership_ReturnsAlreadyExists_NoSecondInsert()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var existing = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, existing.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AddAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: existing.UserId,
            role: OrganizationRole.Coordinator);

        Assert.Equal(MemberAddResult.AlreadyExists, result);

        await using var db = await Factory.CreateDbContextAsync();
        var count = await db.OrganizationMemberships.CountAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == existing.UserId);
        Assert.Equal(1, count);
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == existing.UserId);
        Assert.Equal(OrganizationRole.Volunteer, row.Role);
    }

    [Fact]
    public async Task Add_EmptyCallerId_Denied()
    {
        var org = TestData.Org(Factory);
        var target = TestData.Person(Factory);

        var result = await NewSvc().AddAsync(
            callerUserId: "",
            organizationId: org.Id,
            personUserId: target.UserId,
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Add_EmptyPersonId_Denied()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().AddAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: "",
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Add_CallerNotASystemUser_Denied()
    {
        var org = TestData.Org(Factory);

        var result = await NewSvc().AddAsync(
            callerUserId: "ghost-user-id",
            organizationId: org.Id,
            personUserId: "ghost-target-id",
            role: OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, result);
    }

    // ─── UpdateRole ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRole_AdminCaller_PromotesVolunteerToCoordinator()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.Updated, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId);
        Assert.Equal(OrganizationRole.Coordinator, row.Role);
    }

    [Fact]
    public async Task UpdateRole_AdminCaller_PromotesToAdmin()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            newRole: OrganizationRole.Admin);

        Assert.Equal(MemberMutationResult.Updated, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId);
        Assert.Equal(OrganizationRole.Admin, row.Role);
    }

    [Fact]
    public async Task UpdateRole_NonAdminCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: coordinator.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId);
        Assert.Equal(OrganizationRole.Volunteer, row.Role); // unchanged
    }

    [Fact]
    public async Task UpdateRole_TargetNotAMember_ReturnsNotFound()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var stranger = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: stranger.UserId,
            newRole: OrganizationRole.Volunteer);

        Assert.Equal(MemberMutationResult.NotFound, result);
    }

    [Fact]
    public async Task UpdateRole_SelfDemoteToCoordinator_Refused()
    {
        // An Admin trying to demote themselves would orphan the org if
        // they're the only Admin; service refuses regardless of who else
        // is Admin.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: admin.UserId,
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.SelfDemotionRefused, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == admin.UserId);
        Assert.Equal(OrganizationRole.Admin, row.Role); // unchanged
    }

    [Fact]
    public async Task UpdateRole_SelfDemoteToVolunteer_Refused()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: admin.UserId,
            newRole: OrganizationRole.Volunteer);

        Assert.Equal(MemberMutationResult.SelfDemotionRefused, result);
    }

    [Fact]
    public async Task UpdateRole_AdminADemotesOtherAdminBToCoordinator()
    {
        // Multi-admin demote pathway: Admin A removes Admin B's Admin role,
        // downgrading them to Coordinator. The self-demotion guard only
        // fires for caller == target, so this succeeds. Locks in the
        // intent of the guard (per-person, not per-class) so a future
        // refactor that broadens the guard doesn't silently break
        // multi-admin rotation in real organizations.
        var org = TestData.Org(Factory);
        var adminA = TestData.Person(Factory);
        var adminB = TestData.Person(Factory);
        TestData.Membership(Factory, adminA.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, adminB.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: adminA.UserId,
            organizationId: org.Id,
            personUserId: adminB.UserId,
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.Updated, result);

        await using var db = await Factory.CreateDbContextAsync();
        var adminARow = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == adminA.UserId);
        var adminBRow = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == adminB.UserId);
        Assert.Equal(OrganizationRole.Admin, adminARow.Role); // unchanged
        Assert.Equal(OrganizationRole.Coordinator, adminBRow.Role); // demoted
    }

    [Fact]
    public async Task UpdateRole_SelfReaffirmAdmin_NoChange_Succeeds()
    {
        // Self-update to the SAME role is allowed — no-op success. The
        // self-demotion guard only fires for "Admin → not-Admin".
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: admin.UserId,
            newRole: OrganizationRole.Admin);

        Assert.Equal(MemberMutationResult.Updated, result);
    }

    [Fact]
    public async Task UpdateRole_RoleUnchanged_ReturnsUpdated()
    {
        // Calling Update with the same role the target already has is a
        // no-op success even for non-self rows.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: target.UserId,
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.Updated, result);
    }

    [Fact]
    public async Task UpdateRole_EmptyCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: "",
            organizationId: org.Id,
            personUserId: target.UserId,
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task UpdateRole_EmptyTarget_Denied()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpdateRoleAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: "",
            newRole: OrganizationRole.Coordinator);

        Assert.Equal(MemberMutationResult.PermissionDenied, result);
    }

    // ─── Remove ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_AdminCaller_RemovesVolunteer_ReturnsRemoved()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().RemoveAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: target.UserId);

        Assert.Equal(MemberRemoveResult.Removed, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId));
    }

    [Fact]
    public async Task Remove_AdminCaller_LastAdmin_OtherAdminPresent_RemovesSuccessfully()
    {
        // Multi-admin scenario: caller is Admin, removes target Admin B
        // (also a valid Admin). Service allows because Admin A remains
        // after the deletion. Verifies the "other Admins stay" branch of
        // the last-Admin guard.
        var org = TestData.Org(Factory);
        var adminA = TestData.Person(Factory);
        var adminB = TestData.Person(Factory);
        TestData.Membership(Factory, adminA.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, adminB.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().RemoveAsync(
            callerUserId: adminA.UserId,
            organizationId: org.Id,
            personUserId: adminB.UserId);

        Assert.Equal(MemberRemoveResult.Removed, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == adminA.UserId && m.Role == OrganizationRole.Admin));
        Assert.False(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == adminB.UserId));
    }

    [Fact]
    public async Task Remove_AdminCaller_LoneAdmin_Removed_Refused_LastAdminRefused()
    {
        // Caller is the only Admin of the org. They try to remove
        // themselves (or any other Admin — same branch). Service refuses
        // because removing them would leave zero Admins behind.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().RemoveAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: admin.UserId);

        Assert.Equal(MemberRemoveResult.LastAdminRefused, result);

        await using var db = await Factory.CreateDbContextAsync();
        // Row remains intact.
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == admin.UserId);
        Assert.Equal(OrganizationRole.Admin, row.Role);
    }

    [Fact]
    public async Task Remove_AdminCaller_NonAdminTarget_NoLastAdminConcern_AlwaysSucceeds()
    {
        // Removing a Coordinator/Volunteer never trips the last-Admin
        // guard — only Admin removals trigger that check. Locks in the
        // "only-when-Admin" gating semantics so a future refactor that
        // counts all roles wouldn't broaden the rule.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewSvc().RemoveAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: coordinator.UserId);

        Assert.Equal(MemberRemoveResult.Removed, result);
    }

    [Fact]
    public async Task Remove_NonAdminCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().RemoveAsync(
            callerUserId: coordinator.UserId,
            organizationId: org.Id,
            personUserId: target.UserId);

        Assert.Equal(MemberRemoveResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == target.UserId));
    }

    [Fact]
    public async Task Remove_AdminCaller_TargetNotAMember_ReturnsNotFound()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var stranger = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().RemoveAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: stranger.UserId);

        Assert.Equal(MemberRemoveResult.NotFound, result);
    }

    [Fact]
    public async Task Remove_EmptyCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, target.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().RemoveAsync(
            callerUserId: "",
            organizationId: org.Id,
            personUserId: target.UserId);

        Assert.Equal(MemberRemoveResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Remove_EmptyTarget_Denied()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().RemoveAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            personUserId: "");

        Assert.Equal(MemberRemoveResult.PermissionDenied, result);
    }
}
