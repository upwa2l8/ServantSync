using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-level tests for <see cref="CoordinatorAssignmentsService"/>.
/// Pins the dashboard aggregation (every slot across the org, unassigned
/// first), the per-row assign permission gate, the cross-org membership
/// guard on coordinator person, and the unassign-clears-all-three
/// shortcut. Mirrors the OrgAuthServiceTests' SqliteTestBase style.
/// </summary>
public class CoordinatorAssignmentsServiceTests : SqliteTestBase
{
    private CoordinatorAssignmentsService NewSvc() => new(Factory, new OrgAuthService(Factory));

    // ─── ListAsync (org-scoped aggregate, unassigned-first sort) ────────

    [Fact]
    public async Task ListAsync_ReturnsAllSlotsInOrg()
    {
        var org = TestData.Org(Factory, "Org 7");
        var min1 = TestData.Ministry(Factory, org.Id, "Greeters");
        var min2 = TestData.Ministry(Factory, org.Id, "Sound Tech");
        TestData.Slot(Factory, min1.Id, "Welcome Desk");
        TestData.Slot(Factory, min2.Id, "Sunday Sound");
        TestData.Slot(Factory, min2.Id, "Wednesday Sound");

        var rows = await NewSvc().ListAsync(org.Id);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(org.Id, r.OrganizationId));
        Assert.Contains(rows, r => r.SlotName == "Welcome Desk" && r.MinistryName == "Greeters");
        Assert.Contains(rows, r => r.SlotName == "Sunday Sound" && r.MinistryName == "Sound Tech");
    }

    [Fact]
    public async Task ListAsync_ExcludesSlotsFromOtherOrgs()
    {
        // Org A's dashboard must not leak Org B's slots. The query
        // joins through Ministry → Organization; a stale or hostile
        // org id provided to the dashboard fetch should return only
        // that org's slots.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var minA = TestData.Ministry(Factory, orgA.Id, "Greeters");
        var minB = TestData.Ministry(Factory, orgB.Id, "Confidential");
        TestData.Slot(Factory, minA.Id, "Visible");
        TestData.Slot(Factory, minB.Id, "Hidden");

        var rows = await NewSvc().ListAsync(orgA.Id);

        Assert.Single(rows);
        Assert.Equal("Visible", rows[0].SlotName);
    }

    [Fact]
    public async Task ListAsync_SortsUnassignedFirst()
    {
        // Default sort: unassigned slots come first so the
        // dashboard's "what still needs attention" lens surfaces
        // them at the top. Tied within assigned/unassigned groups
        // the secondary sort is by ministry name then slot name.
        var org = TestData.Org(Factory);
        var minA = TestData.Ministry(Factory, org.Id, "Alpha");
        var minB = TestData.Ministry(Factory, org.Id, "Beta");
        var assignedSlot = TestData.Slot(Factory, minA.Id, "A-Assigned");
        var unassignedSlot = TestData.Slot(Factory, minB.Id, "B-Unassigned");
        var coord = TestData.Person(Factory);
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Volunteer);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var s = await db.ServiceSlots.FindAsync(assignedSlot.Id);
            s!.CoordinatorPersonUserId = coord.UserId;
            s.CoordinatorEmail = "coord@test.local";
            await db.SaveChangesAsync();
        }

        var rows = await NewSvc().ListAsync(org.Id);

        Assert.Equal(2, rows.Count);
        Assert.Equal("B-Unassigned", rows[0].SlotName);
        Assert.Equal("A-Assigned", rows[1].SlotName);
    }

    [Fact]
    public async Task ListAsync_CoordinatorRowShowsDisplayName()
    {
        // A slot with a coordinator FK should show the Person's
        // display name in the row, not just the FK string.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id, "Welcome Desk");
        var coord = TestData.Person(Factory, "Sara", "Coordinator");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Volunteer);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var s = await db.ServiceSlots.FindAsync(slot.Id);
            s!.CoordinatorPersonUserId = coord.UserId;
            await db.SaveChangesAsync();
        }

        var rows = await NewSvc().ListAsync(org.Id);

        Assert.Single(rows);
        Assert.Equal(coord.UserId, rows[0].CoordinatorUserId);
        Assert.Contains("Sara", rows[0].CoordinatorDisplayName);
        Assert.Contains("Coordinator", rows[0].CoordinatorDisplayName);
    }

    [Fact]
    public async Task ListAsync_EmptyOrgReturnsEmptyList()
    {
        var org = TestData.Org(Factory, "Empty Org");
        var rows = await NewSvc().ListAsync(org.Id);
        Assert.Empty(rows);
    }

    // ─── AssignAsync (per-row mutation gate) ─────────────────────────────

    [Fact]
    public async Task AssignAsync_Admin_UpdatesAllThreeFields()
    {
        // Full triple update path: admin sets FK + email + phone
        // and the service persists all three.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        var coord = TestData.Person(Factory);
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AssignAsync(
            slot.Id, coord.UserId, "coord@test.local", "+1-555-1234", admin.UserId);

        Assert.Equal(CoordinatorMutationResult.Updated, result);
        await using var db = await Factory.CreateDbContextAsync();
        var after = await db.ServiceSlots.AsNoTracking().FirstAsync(s => s.Id == slot.Id);
        Assert.Equal(coord.UserId, after.CoordinatorPersonUserId);
        Assert.Equal("coord@test.local", after.CoordinatorEmail);
        Assert.Equal("+1-555-1234", after.CoordinatorPhone);
    }

    [Fact]
    public async Task AssignAsync_OrgCoordinator_Allowed()
    {
        // Per the existing RBAC matrix, Coordinator role can
        // also delegate slot coordinators (matches the rest of
        // the org surface).
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var caller = TestData.Person(Factory);
        TestData.Membership(Factory, caller.UserId, org.Id, OrganizationRole.Coordinator);
        var coord = TestData.Person(Factory);
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AssignAsync(
            slot.Id, coord.UserId, null, null, caller.UserId);

        Assert.Equal(CoordinatorMutationResult.Updated, result);
    }

    [Fact]
    public async Task AssignAsync_Volunteer_Refused()
    {
        // A volunteer-only member of the org must NOT be able to
        // assign themselves or anyone else as a slot coordinator
        // — that's an admin-tier decision.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AssignAsync(slot.Id, volunteer.UserId, null, null, volunteer.UserId);

        Assert.Equal(CoordinatorMutationResult.PermissionDenied, result);
        await using var db = await Factory.CreateDbContextAsync();
        var after = await db.ServiceSlots.AsNoTracking().FirstAsync(s => s.Id == slot.Id);
        Assert.Null(after.CoordinatorPersonUserId);
    }

    [Fact]
    public async Task AssignAsync_NotOrgMember_Refused()
    {
        // Stranger with no membership at all can never assign.
        // Prevents a hostile URL probe from accidentally granting
        // coordinator on a slot in an org the caller doesn't own.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var stranger = TestData.Person(Factory);

        var result = await NewSvc().AssignAsync(slot.Id, stranger.UserId, null, null, stranger.UserId);

        Assert.Equal(CoordinatorMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task AssignAsync_WrongOrgCaller_Refused()
    {
        // Caller is an Admin of Org B but the slot lives in Org A.
        // Org membership is per-organization, so cross-org Admin
        // can't poke slots in a different org.
        var orgA = TestData.Org(Factory, "Org A");
        var min = TestData.Ministry(Factory, orgA.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var orgBAdmin = TestData.Person(Factory);
        var orgB = TestData.Org(Factory, "Org B");
        TestData.Membership(Factory, orgBAdmin.UserId, orgB.Id, OrganizationRole.Admin);

        var result = await NewSvc().AssignAsync(slot.Id, orgBAdmin.UserId, null, null, orgBAdmin.UserId);

        Assert.Equal(CoordinatorMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task AssignAsync_CoordinatorMustBeOrgMember_Refused()
    {
        // The Person row being assigned must live in the same org
        // as the slot. Surprise would otherwise be a Foreign-org
        // volunteer shown as "coordinator" of an unrelated org's
        // slot — confusing in the dashboard.
        var org = TestData.Org(Factory, "Our Org");
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var otherOrgsPerson = TestData.Person(Factory);
        var otherOrg = TestData.Org(Factory, "Other Org");
        TestData.Membership(Factory, otherOrgsPerson.UserId, otherOrg.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().AssignAsync(slot.Id, otherOrgsPerson.UserId, null, null, admin.UserId);

        Assert.Equal(CoordinatorMutationResult.PermissionDenied, result);
        await using var db = await Factory.CreateDbContextAsync();
        var after = await db.ServiceSlots.AsNoTracking().FirstAsync(s => s.Id == slot.Id);
        Assert.Null(after.CoordinatorPersonUserId);
    }

    [Fact]
    public async Task AssignAsync_NullUserId_ClearsFK()
    {
        // Empty UserId is always allowed for an admin — it's how
        // the admin explicitly removes the coordinator FK without
        // picking a replacement.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        // Seed an existing coordinator first.
        var existing = TestData.Person(Factory);
        TestData.Membership(Factory, existing.UserId, org.Id, OrganizationRole.Volunteer);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var s = await db.ServiceSlots.FindAsync(slot.Id);
            s!.CoordinatorPersonUserId = existing.UserId;
            await db.SaveChangesAsync();
        }

        var result = await NewSvc().AssignAsync(slot.Id, "", "new@test.local", null, admin.UserId);

        Assert.Equal(CoordinatorMutationResult.Updated, result);
        await using var db2 = await Factory.CreateDbContextAsync();
        var after = await db2.ServiceSlots.AsNoTracking().FirstAsync(s => s.Id == slot.Id);
        Assert.Null(after.CoordinatorPersonUserId);
        Assert.Equal("new@test.local", after.CoordinatorEmail);
    }

    [Fact]
    public async Task AssignAsync_EmptyCaller_Refused()
    {
        // The anonymous-claim guard.
        var slot = TestData.Slot(Factory, TestData.Ministry(Factory, TestData.Org(Factory).Id).Id);
        var result = await NewSvc().AssignAsync(slot.Id, null, null, null, "");
        Assert.Equal(CoordinatorMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task AssignAsync_UnknownSlot_NotFound()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().AssignAsync(slotId: 99_999, null, null, null, admin.UserId);

        Assert.Equal(CoordinatorMutationResult.NotFound, result);
    }

    // ─── UnassignAsync (clears all three fields in one call) ─────────────

    [Fact]
    public async Task UnassignAsync_Admin_ClearsAllThreeFields()
    {
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        var coord = TestData.Person(Factory);
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Volunteer);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var s = await db.ServiceSlots.FindAsync(slot.Id);
            s!.CoordinatorPersonUserId = coord.UserId;
            s.CoordinatorEmail = "coord@test.local";
            s.CoordinatorPhone = "+1-555-1234";
            await db.SaveChangesAsync();
        }

        var result = await NewSvc().UnassignAsync(slot.Id, admin.UserId);

        Assert.Equal(CoordinatorMutationResult.Updated, result);
        await using var db2 = await Factory.CreateDbContextAsync();
        var after = await db2.ServiceSlots.AsNoTracking().FirstAsync(s => s.Id == slot.Id);
        Assert.Null(after.CoordinatorPersonUserId);
        Assert.Null(after.CoordinatorEmail);
        Assert.Null(after.CoordinatorPhone);
    }

    [Fact]
    public async Task UnassignAsync_Volunteer_Refused()
    {
        // Same gate as AssignAsync applies.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, min.Id);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var coord = TestData.Person(Factory);
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Volunteer);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var s = await db.ServiceSlots.FindAsync(slot.Id);
            s!.CoordinatorPersonUserId = coord.UserId;
            await db.SaveChangesAsync();
        }

        var result = await NewSvc().UnassignAsync(slot.Id, volunteer.UserId);

        Assert.Equal(CoordinatorMutationResult.PermissionDenied, result);
        await using var db2 = await Factory.CreateDbContextAsync();
        var after = await db2.ServiceSlots.AsNoTracking().FirstAsync(s => s.Id == slot.Id);
        Assert.Equal(coord.UserId, after.CoordinatorPersonUserId);
    }
}
