using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="MinistryInterestService"/>.
/// Drives the new volunteer "join a ministry" preference feature:
/// strict per-org permission gate, idempotent join (returns
/// <see cref="MinistryInterestJoinResult.AlreadyJoined"/> rather than
/// surfacing the unique-index DB error), and the
/// <see cref="MinistryInterestService.ListJoinedAsync"/> read path.
/// </summary>
public class MinistryInterestServiceTests : SqliteTestBase
{
    private MinistryInterestService NewService() =>
        new(Factory, NullLogger<MinistryInterestService>.Instance);

    // ─── JoinAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task JoinAsync_NewMember_ReturnsJoined()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().JoinAsync(user.UserId, user.UserId, ministry.Id);

        Assert.Equal(MinistryInterestJoinResult.Joined, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.MinistryInterests
            .AnyAsync(i => i.PersonUserId == user.UserId && i.MinistryId == ministry.Id));
    }

    [Fact]
    public async Task JoinAsync_Duplicate_ReturnsAlreadyJoinedAndDoesntError()
    {
        // Two sequential JoinAsync calls: first inserts, second returns
        // AlreadyJoined without throwing on the unique-index violation.
        // This is the safety net the service-level pre-check exists to
        // avoid; an in-flight concurrent insert that races past the check
        // is caught by the DB unique index and treated identically.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        var first = await svc.JoinAsync(user.UserId, user.UserId, ministry.Id);
        var second = await svc.JoinAsync(user.UserId, user.UserId, ministry.Id);

        Assert.Equal(MinistryInterestJoinResult.Joined, first);
        Assert.Equal(MinistryInterestJoinResult.AlreadyJoined, second);
    }

    [Fact]
    public async Task JoinAsync_CrossOrgCaller_ReturnsPermissionDenied()
    {
        // Strict per-org sandbox: a volunteer in Org A must NOT be able to
        // follow a ministry in Org B (which would surface Org B's open
        // shifts in their /Open list).
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var ministryB = TestData.Ministry(Factory, orgB.Id);
        var userA = TestData.Person(Factory);
        TestData.Membership(Factory, userA.UserId, orgA.Id, OrganizationRole.Volunteer);

        var result = await NewService().JoinAsync(userA.UserId, userA.UserId, ministryB.Id);

        Assert.Equal(MinistryInterestJoinResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.MinistryInterests
            .AnyAsync(i => i.PersonUserId == userA.UserId && i.MinistryId == ministryB.Id));
    }

    [Fact]
    public async Task JoinAsync_CallerNotInAnyOrg_ReturnsPermissionDenied()
    {
        // Caller exists (with a Person row) but is not a member of any
        // organization. Even if the ministry is in some org, the in-org
        // gate fails — return PermissionDenied.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var orphan = TestData.Person(Factory); // no Membership row created

        var result = await NewService().JoinAsync(orphan.UserId, orphan.UserId, ministry.Id);

        Assert.Equal(MinistryInterestJoinResult.PermissionDenied, result);
    }

    [Fact]
    public async Task JoinAsync_AdminOfOwningOrg_CanJoin()
    {
        // Admins are regular members; they can join a ministry they
        // manage (the user clarification was that Coordinators do NOT
        // auto-follow but CAN explicitly opt in, same as anyone else).
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewService().JoinAsync(admin.UserId, admin.UserId, ministry.Id);

        Assert.Equal(MinistryInterestJoinResult.Joined, result);
    }

    [Fact]
    public async Task JoinAsync_CoordinatorOfOwningOrg_CanJoin()
    {
        // Same as the admin case — Coordinators manage ministries via the
        // service gate but their personal interest is independent and
        // opt-in like everyone else's.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewService().JoinAsync(coordinator.UserId, coordinator.UserId, ministry.Id);

        Assert.Equal(MinistryInterestJoinResult.Joined, result);
    }

    [Fact]
    public async Task JoinAsync_AdminOfOwningOrg_InsertForOutOfOrgTarget_Allowed()
    {
        // Pins the explicit API surface: the gate checks the CALLER's
        // org membership (not the target's). When caller != target,
        // only the caller's membership decides. The UI only ever calls
        // JoinAsync(_userId, _userId, …) — self-join — but the interior
        // API surface is wider: an Admin of Org A can technically insert
        // a row for any PersonUserId (including someone not in Org A).
        // This test documents the existing behavior so any future
        // tightening (or accidental widening) either updates both
        // implementation + tests or faces a deliberate review.
        var orgA = TestData.Org(Factory);
        var ministryA = TestData.Ministry(Factory, orgA.Id);
        var callerAdmin = TestData.Person(Factory, "CallerAdmin", "OfA");
        var targetVolunteer = TestData.Person(Factory, "TargetVol", "Elsewhere");
        TestData.Membership(Factory, callerAdmin.UserId, orgA.Id, OrganizationRole.Admin);
        // Note: targetVolunteer is NOT a member of orgA — Admin of A
        // can still insert the row because the gate is on the caller.

        var result = await NewService().JoinAsync(
            callerAdmin.UserId, targetVolunteer.UserId, ministryA.Id);

        Assert.Equal(MinistryInterestJoinResult.Joined, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.MinistryInterests
            .AnyAsync(i => i.PersonUserId == targetVolunteer.UserId && i.MinistryId == ministryA.Id));
    }

    [Fact]
    public async Task JoinAsync_CoordinatorOfOwningOrg_InsertForOutOfOrgTarget_Allowed_GateIsRoleAgnostic()
    {
        // Companion invariant to the Admin test: the gate is
        // org-membership-only, NOT role-specific. A Coordinator of Org A
        // succeeds the same way an Admin of Org A does when caller !=
        // target — even though Coordinators do not have admin powers on
        // org management, the JOIN gate doesn't differentiate. The pinning
        // matters because any future refactor that adds a role-check on
        // top must update the gate AND this test together. The follow-up
        // DB read here mirrors the Admin-tier test so a hypothetical
        // SaveChangesAsync branch on role would surface equally on both
        // tiers.
        var orgA = TestData.Org(Factory);
        var ministryA = TestData.Ministry(Factory, orgA.Id);
        var callerCoordinator = TestData.Person(Factory, "CallerCoord", "OfA");
        var targetVolunteer = TestData.Person(Factory, "TargetVol", "Elsewhere");
        TestData.Membership(Factory, callerCoordinator.UserId, orgA.Id, OrganizationRole.Coordinator);

        var result = await NewService().JoinAsync(
            callerCoordinator.UserId, targetVolunteer.UserId, ministryA.Id);

        Assert.Equal(MinistryInterestJoinResult.Joined, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.MinistryInterests
            .AnyAsync(i => i.PersonUserId == targetVolunteer.UserId && i.MinistryId == ministryA.Id));
    }

    [Fact]
    public async Task JoinAsync_CallerInOtherOrg_TargetInOwningOrg_Denied()
    {
        // Companion invariant: when caller is in some OTHER org, even if
        // the target IS a member of the ministry's org, the gate fires
        // on the caller and refuses. This guards against an asymmetric
        // "if either side is in the org, allow" interpretation creeping
        // in via refactor.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var ministryA = TestData.Ministry(Factory, orgA.Id);
        var callerInOtherOrg = TestData.Person(Factory, "Caller", "Other");
        var targetInA = TestData.Person(Factory, "Target", "InA");
        TestData.Membership(Factory, callerInOtherOrg.UserId, orgB.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, targetInA.UserId, orgA.Id, OrganizationRole.Volunteer);

        var result = await NewService().JoinAsync(
            callerInOtherOrg.UserId, targetInA.UserId, ministryA.Id);

        Assert.Equal(MinistryInterestJoinResult.PermissionDenied, result);
    }

    [Fact]
    public async Task JoinAsync_NonexistentMinistry_ReturnsMinistryNotFound()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().JoinAsync(user.UserId, user.UserId, ministryId: 9999);

        Assert.Equal(MinistryInterestJoinResult.MinistryNotFound, result);
    }

    [Fact]
    public async Task JoinAsync_EmptyCallerPersonUserId_ReturnsPermissionDenied()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);

        // Both null and empty should be treated identically as
        // PermissionDenied (avoids a NullReferenceException out of the DB
        // and avoids surfacing AlreadyJoined when there's nothing to join).
        Assert.Equal(MinistryInterestJoinResult.PermissionDenied,
            await NewService().JoinAsync(callerUserId: null, personUserId: null, ministryId: ministry.Id));
        Assert.Equal(MinistryInterestJoinResult.PermissionDenied,
            await NewService().JoinAsync(callerUserId: "", personUserId: "", ministryId: ministry.Id));
    }

    // ─── LeaveAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveAsync_AfterJoin_ReturnsLeft()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        await svc.JoinAsync(user.UserId, user.UserId, ministry.Id);

        var result = await svc.LeaveAsync(user.UserId, user.UserId, ministry.Id);

        Assert.Equal(MinistryInterestLeaveResult.Left, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.MinistryInterests
            .AnyAsync(i => i.PersonUserId == user.UserId && i.MinistryId == ministry.Id));
    }

    [Fact]
    public async Task LeaveAsync_WhenNotJoined_ReturnsNotInterested()
    {
        // The user hasn't joined this ministry yet → LeaveAsync is a
        // no-op. Returning NotInterested rather than an exception lets
        // the UI say "you weren't following this ministry" without a
        // crash on double-clicks.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().LeaveAsync(user.UserId, user.UserId, ministry.Id);

        Assert.Equal(MinistryInterestLeaveResult.NotInterested, result);
    }

    [Fact]
    public async Task LeaveAsync_CrossOrgCaller_ReturnsPermissionDenied()
    {
        // Even though the interest row belongs to userA, an out-of-org
        // caller (userC) cannot delete it. The gate catches both
        // "self-leave not in target org" and "destructive-by-proxy"
        // attempts.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var orgC = TestData.Org(Factory, "Org C");
        var ministryB = TestData.Ministry(Factory, orgB.Id);
        var userA = TestData.Person(Factory);
        var userC = TestData.Person(Factory);
        TestData.Membership(Factory, userA.UserId, orgA.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, userC.UserId, orgC.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        // userA is in orgA but the ministry is in orgB → first call denied
        var try1 = await svc.LeaveAsync(userA.UserId, userA.UserId, ministryB.Id);
        Assert.Equal(MinistryInterestLeaveResult.PermissionDenied, try1);

        // userC is in neither the ministry's org nor can match the user's
        // own "leave on someone's behalf" gate → PermissionDenied.
        var try2 = await svc.LeaveAsync(userC.UserId, userA.UserId, ministryB.Id);
        Assert.Equal(MinistryInterestLeaveResult.PermissionDenied, try2);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.MinistryInterests.AnyAsync());
    }

    [Fact]
    public async Task LeaveAsync_NonexistentMinistry_ReturnsMinistryNotFound()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().LeaveAsync(user.UserId, user.UserId, ministryId: 9999);

        Assert.Equal(MinistryInterestLeaveResult.MinistryNotFound, result);
    }

    // ─── ListJoinedAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListJoinedAsync_IncludesEagerLoadedMinistryAndOrg()
    {
        // Critical for the Home panel: the single-trip eager-loaded shape
        // feeds the entire list with no per-row lazy load chasing.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id, "Worship");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        await NewService().JoinAsync(user.UserId, user.UserId, ministry.Id);

        var list = await NewService().ListJoinedAsync(user.UserId);

        Assert.Single(list);
        var row = list[0];
        Assert.NotNull(row.Ministry);
        Assert.Equal("Worship", row.Ministry.Name);
        Assert.NotNull(row.Ministry.Organization);
        Assert.Equal(org.Id, row.Ministry.Organization.Id);
    }

    [Fact]
    public async Task ListJoinedAsync_SortedByMinistryNameAscending()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        // Insert in non-alphabetical order to prove the service sorts.
        var ministryZ = TestData.Ministry(Factory, org.Id, "Zebra");
        var ministryA = TestData.Ministry(Factory, org.Id, "Alpha");
        var ministryM = TestData.Ministry(Factory, org.Id, "Mango");

        var svc = NewService();
        await svc.JoinAsync(user.UserId, user.UserId, ministryZ.Id);
        await svc.JoinAsync(user.UserId, user.UserId, ministryA.Id);
        await svc.JoinAsync(user.UserId, user.UserId, ministryM.Id);

        var list = await svc.ListJoinedAsync(user.UserId);

        Assert.Equal(new[] { "Alpha", "Mango", "Zebra" }, list.Select(i => i.Ministry.Name).ToArray());
    }

    [Fact]
    public async Task ListJoinedAsync_FiltersByUserId()
    {
        // A user's list shows ONLY their own interests, not any other
        // volunteer's. (Sanity check — distinct from the eager-load test.)
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);

        var userMine = TestData.Person(Factory, "Mine", "V");
        var userTheirs = TestData.Person(Factory, "Theirs", "V");
        TestData.Membership(Factory, userMine.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, userTheirs.UserId, org.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        // UserMine follows the ministry; userTheirs doesn't.
        await svc.JoinAsync(userMine.UserId, userMine.UserId, ministry.Id);

        Assert.Single(await svc.ListJoinedAsync(userMine.UserId));
        Assert.Empty(await svc.ListJoinedAsync(userTheirs.UserId));
    }

    [Fact]
    public async Task ListJoinedAsync_EmptyUserId_ReturnsEmpty()
    {
        // Empty/no-userId sentinel: a defensive empty result rather than
        // surfacing a null cast or throwing.
        Assert.Empty(await NewService().ListJoinedAsync(""));
        Assert.Empty(await NewService().ListJoinedAsync(null!));
    }

    [Fact]
    public async Task ListJoinedAsync_CrossOrgInterestsExcludedByDefault_ServiceHasNoOrgFilter()
    {
        // Document an explicit invariant: ListJoinedAsync returns the
        // user's rows regardless of which org owns the ministry. The
        // org-sandbox gate lives at the write side (JoinAsync /
        // LeaveAsync); this read is "what did this user opt into".
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var ministryA = TestData.Ministry(Factory, orgA.Id, "A-Min");
        var ministryB = TestData.Ministry(Factory, orgB.Id, "B-Min");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        await svc.JoinAsync(user.UserId, user.UserId, ministryA.Id);
        await svc.JoinAsync(user.UserId, user.UserId, ministryB.Id);

        var list = await svc.ListJoinedAsync(user.UserId);
        Assert.Equal(2, list.Count);
    }
}
