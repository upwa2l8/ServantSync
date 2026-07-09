using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="SlotInterestService"/>.
/// Mirrors <c>MinistryInterestServiceTests</c>'s RBAC matrix (the
/// sibling-level preference service is the canonical mirror reference
/// per Round-FR-7 spec Q-A). Each test sets up a real in-memory SQLite
/// DbContext via SqliteTestBase's <c>:memory:</c> connection and
/// exercises the actual EF Core call paths so the schema (composite
/// unique index, FK cascades, slot→ministry→org navigation) is
/// verified live — not mocked.
/// </summary>
public class SlotInterestServiceTests : SqliteTestBase
{
    private SlotInterestService NewService() =>
        new(Factory, NullLogger<SlotInterestService>.Instance);

    // ─── SubscribeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_OrgMemberOnSlot_Subscribes()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var alice = TestData.Person(Factory, firstName: "Alice", userId: "user-alice");
        TestData.Membership(Factory, alice.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(alice.UserId, alice.UserId, slot.Id);

        Assert.Equal(SlotInterestJoinResult.Subscribed, result);
    }

    [Fact]
    public async Task SubscribeAsync_MinistryDirectorOnSlot_Subscribes_RoleIsPermissive()
    {
        // Per FR-7 spec, the Subscribe gate is OrgMember (any role can
        // subscribe themselves) — distinct from cross-person Unsubscribe
        // which is coordinate-gated. Mirror MinistryInterestService's
        // permissive model for self-service consistency.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var director = TestData.Person(Factory, firstName: "Bob", userId: "user-bob");
        TestData.Membership(Factory, director.UserId, org.Id, OrganizationRole.MinistryDirector);

        var result = await NewService().SubscribeAsync(director.UserId, director.UserId, slot.Id);

        Assert.Equal(SlotInterestJoinResult.Subscribed, result);
    }

    [Fact]
    public async Task SubscribeAsync_CallerNotInOrg_PermissionDenied()
    {
        var orgA = TestData.Org(Factory, "OrgA");
        var orgB = TestData.Org(Factory, "OrgB");
        var ministry = TestData.Ministry(Factory, orgA.Id, "M");
        var slot = TestData.Slot(Factory, ministry.Id);
        var stranger = TestData.Person(Factory, firstName: "Stranger", userId: "user-stranger");
        TestData.Membership(Factory, stranger.UserId, orgB.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(stranger.UserId, stranger.UserId, slot.Id);

        Assert.Equal(SlotInterestJoinResult.PermissionDenied, result);

        // Negative assertion: no row created in OrgA.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(db.SlotInterests);
    }

    [Fact]
    public async Task SubscribeAsync_SlotNotFound_SlotNotFound()
    {
        var org = TestData.Org(Factory);
        var user = TestData.Person(Factory, firstName: "Dan", userId: "user-dan");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(user.UserId, user.UserId, slotId: 9999);

        Assert.Equal(SlotInterestJoinResult.SlotNotFound, result);
    }

    [Fact]
    public async Task SubscribeAsync_NullCallerUserId_PermissionDenied()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Eve", userId: "user-eve");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(callerUserId: null, personUserId: user.UserId, slotId: slot.Id);

        Assert.Equal(SlotInterestJoinResult.PermissionDenied, result);
    }

    [Fact]
    public async Task SubscribeAsync_EmptyCallerUserId_PermissionDenied()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Frank", userId: "user-frank");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(callerUserId: "", personUserId: user.UserId, slotId: slot.Id);

        Assert.Equal(SlotInterestJoinResult.PermissionDenied, result);
    }

    [Fact]
    public async Task SubscribeAsync_AdminOfOwningOrg_InsertForOutOfOrgTarget_Allowed_GateIsOrgMembershipOnly()
    {
        // Mirrors MinistryInterestServiceTests's pinned invariant: the
        // gate is caller-OrgMembership, NOT role-specific. When caller
        // != target, only the caller's org membership decides. The UI
        // only ever calls SubscribeAsync(_userId, _userId, …) — self-
        // subscribe — but the interior API surface is wider: an Admin
        // of Org A can technically insert a row for someone not in Org
        // A. This test pins the existing behavior so any future
        // tightening (or accidental widening) surfaces asymmetrically.
        // The follow-up DB read here mirrors the Admin-tier
        // MinistryInterest test so any hypothetical future
        // SaveChangesAsync branch on role would surface equally on
        // both tier 1 (Admin) and tier 2 (SlotCoordinator) tiers.
        var orgA = TestData.Org(Factory, "OrgA");
        var ministryInA = TestData.Ministry(Factory, orgA.Id, "M-In-A");
        var slotInA = TestData.Slot(Factory, ministryInA.Id, "S-In-A");
        var callerAdmin = TestData.Person(Factory, firstName: "Caller", lastName: "AdminOfA", userId: "user-admin-a");
        var targetVolunteer = TestData.Person(Factory, firstName: "Target", lastName: "Elsewhere", userId: "user-target-elsewhere");
        TestData.Membership(Factory, callerAdmin.UserId, orgA.Id, OrganizationRole.Admin);
        // Note: targetVolunteer is NOT a member of orgA — Admin of A
        // can still insert because the gate is on the caller.

        var result = await NewService().SubscribeAsync(
            callerAdmin.UserId, targetVolunteer.UserId, slotInA.Id);

        Assert.Equal(SlotInterestJoinResult.Subscribed, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(db.SlotInterests.Any(
            i => i.PersonUserId == targetVolunteer.UserId && i.ServiceSlotId == slotInA.Id));
    }

    [Fact]
    public async Task SubscribeAsync_NullPersonUserId_PermissionDenied()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Gail", userId: "user-gail");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(callerUserId: user.UserId, personUserId: null, slotId: slot.Id);

        Assert.Equal(SlotInterestJoinResult.PermissionDenied, result);
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateSubscribe_AlreadySubscribed()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Hank", userId: "user-hank");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var first = await NewService().SubscribeAsync(user.UserId, user.UserId, slot.Id);
        var second = await NewService().SubscribeAsync(user.UserId, user.UserId, slot.Id);

        Assert.Equal(SlotInterestJoinResult.Subscribed, first);
        Assert.Equal(SlotInterestJoinResult.AlreadySubscribed, second);

        // Negative: only one row landed despite two calls.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Single(db.SlotInterests);
    }

    [Fact]
    public async Task SubscribeAsync_WithAutoFromAssignmentSource_PersistsSourceColumn()
    {
        // Verifies FR-7 spec Q-B1's audit-trail column round-trips. The
        // /Open auto-subscribe path passes AutoFromAssignment; this
        // asserts the row's Source value matches.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Ivy", userId: "user-ivy");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SubscribeAsync(
            user.UserId, user.UserId, slot.Id, SlotInterestSource.AutoFromAssignment);

        Assert.Equal(SlotInterestJoinResult.Subscribed, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = db.SlotInterests.Single();
        Assert.Equal(SlotInterestSource.AutoFromAssignment, row.Source);
        Assert.Equal(user.UserId, row.PersonUserId);
        Assert.Equal(slot.Id, row.ServiceSlotId);
    }

    [Fact]
    public async Task SubscribeAsync_DefaultSourceIsExplicit()
    {
        // Explicit is the default — the volunteer-clicked-Subscribe-button
        // path doesn't specify a source, so this guards against future
        // accidental default-flip.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Jay", userId: "user-jay");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SubscribeAsync(user.UserId, user.UserId, slot.Id);  // no source arg

        await using var db = await Factory.CreateDbContextAsync();
        var row = db.SlotInterests.Single();
        Assert.Equal(SlotInterestSource.Explicit, row.Source);
    }

    // ─── UnsubscribeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UnsubscribeAsync_SubscribedRow_Unsubscribed()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Karen", userId: "user-karen");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        await NewService().SubscribeAsync(user.UserId, user.UserId, slot.Id);

        var result = await NewService().UnsubscribeAsync(user.UserId, user.UserId, slot.Id);

        Assert.Equal(SlotInterestLeaveResult.Unsubscribed, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(db.SlotInterests);
    }

    [Fact]
    public async Task UnsubscribeAsync_NotSubscribed_NotSubscribed()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Liam", userId: "user-liam");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().UnsubscribeAsync(user.UserId, user.UserId, slot.Id);

        Assert.Equal(SlotInterestLeaveResult.NotSubscribed, result);
    }

    [Fact]
    public async Task UnsubscribeAsync_CallerNotInOrg_PermissionDenied()
    {
        var orgA = TestData.Org(Factory, "OrgA");
        var orgB = TestData.Org(Factory, "OrgB");
        var ministry = TestData.Ministry(Factory, orgA.Id, "M");
        var slot = TestData.Slot(Factory, ministry.Id);
        var orgAOwner = TestData.Person(Factory, firstName: "Mia", userId: "user-mia");
        TestData.Membership(Factory, orgAOwner.UserId, orgA.Id, OrganizationRole.Admin);
        await NewService().SubscribeAsync(orgAOwner.UserId, orgAOwner.UserId, slot.Id);

        var orgBStranger = TestData.Person(Factory, firstName: "Noah", userId: "user-noah");
        TestData.Membership(Factory, orgBStranger.UserId, orgB.Id, OrganizationRole.Volunteer);

        var result = await NewService().UnsubscribeAsync(orgBStranger.UserId, orgAOwner.UserId, slot.Id);

        Assert.Equal(SlotInterestLeaveResult.PermissionDenied, result);

        // Negative: subscription row in OrgA is still there.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Single(db.SlotInterests);
    }

    [Fact]
    public async Task UnsubscribeAsync_SlotNotFound_SlotNotFound()
    {
        var user = TestData.Person(Factory, firstName: "Olive", userId: "user-olive");

        var result = await NewService().UnsubscribeAsync(user.UserId, user.UserId, slotId: 9999);

        Assert.Equal(SlotInterestLeaveResult.SlotNotFound, result);
    }

    [Fact]
    public async Task UnsubscribeAsync_NullCallerUserId_PermissionDenied()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id);
        var user = TestData.Person(Factory, firstName: "Pat", userId: "user-pat");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        await NewService().SubscribeAsync(user.UserId, user.UserId, slot.Id);

        var result = await NewService().UnsubscribeAsync(callerUserId: null, personUserId: user.UserId, slotId: slot.Id);

        Assert.Equal(SlotInterestLeaveResult.PermissionDenied, result);

        // Negative: row remains untouched on a permission-denied Unsubscribe.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Single(db.SlotInterests);
    }

    // ─── ListSubscribedAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ListSubscribedAsync_EmptyCallerUserId_EmptyList()
    {
        // Defensive null/empty guard mirroring MinistryInterestService.ListJoinedAsync.
        var result = await NewService().ListSubscribedAsync(personUserId: "");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListSubscribedAsync_TwoSubscriptions_ReturnsBothSortedBySlotName()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slotA = TestData.Slot(Factory, ministry.Id, "Alpha Slot");
        var slotB = TestData.Slot(Factory, ministry.Id, "Bravo Slot");
        var user = TestData.Person(Factory, firstName: "Quincy", userId: "user-quincy");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        await NewService().SubscribeAsync(user.UserId, user.UserId, slotB.Id);  // subscribe Bravo first
        await NewService().SubscribeAsync(user.UserId, user.UserId, slotA.Id);

        var list = await NewService().ListSubscribedAsync(user.UserId);

        Assert.Equal(2, list.Count);
        Assert.Equal(slotA.Id, list[0].ServiceSlotId);
        Assert.Equal(slotB.Id, list[1].ServiceSlotId);
        // Eager-load: ServiceSlot navigation is populated, no N+1.
        Assert.NotNull(list[0].ServiceSlot);
        Assert.NotNull(list[0].ServiceSlot.Ministry);
    }

    // ─── ListForSlotAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListForSlotAsync_TwoSubscribers_ReturnsBothSortedByPersonName()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id, "Greeters");
        // Alphabetical ordering: Adams < Carter
        var adams = TestData.Person(Factory, firstName: "Quincy", lastName: "Adams", userId: "user-adams");
        var carter = TestData.Person(Factory, firstName: "Quincy", lastName: "Carter", userId: "user-carter");
        TestData.Membership(Factory, adams.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, carter.UserId, org.Id, OrganizationRole.Volunteer);
        await NewService().SubscribeAsync(adams.UserId, adams.UserId, slot.Id);
        await NewService().SubscribeAsync(carter.UserId, carter.UserId, slot.Id);

        var list = await NewService().ListForSlotAsync(slot.Id);

        Assert.Equal(2, list.Count);
        Assert.Equal(adams.UserId, list[0].PersonUserId);
        Assert.Equal(carter.UserId, list[1].PersonUserId);
        // Eager-load: Person navigation is populated so the coord
        // Subscribers(N) panel renders display-name + email without
        // N+1. Person.User (the underlying IdentityUser) is intentionally
        // NOT eager-loaded — Person → User has no inverse nav on the
        // IdentityUser side (`.WithOne()`), so a ThenInclude JOIN is
        // brittle across providers. The panel only needs Person data.
        Assert.NotNull(list[0].Person);
        Assert.Equal("Adams", list[0].Person.LastName);
        Assert.Equal("Carter", list[1].Person.LastName);
    }

    [Fact]
    public async Task ListForSlotAsync_ZeroOrNegativeSlotId_EmptyList()
    {
        // Defensive guard mirroring MinistryInterestService.ListForMinistryAsync's
        // `ministryId <= 0 → new()` check. Negative ids would otherwise
        // hit the EF LINQ provider with a nonsense expression.
        Assert.Empty(await NewService().ListForSlotAsync(slotId: 0));
        Assert.Empty(await NewService().ListForSlotAsync(slotId: -1));
    }

    [Fact]
    public async Task ListForSlotAsync_NoSubscribers_EmptyList()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id, "Lonely Slot");

        var list = await NewService().ListForSlotAsync(slot.Id);

        Assert.Empty(list);
    }
}
