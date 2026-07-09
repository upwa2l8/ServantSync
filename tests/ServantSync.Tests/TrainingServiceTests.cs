using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Integration tests for <see cref="TrainingService" /> covering the new
/// volunteer-centric aggregate methods (<c>FindMyOutstandingRequirementsAsync</c>,
/// <c>ListMyHistoryAsync</c>) and the per-org overload that Home/MySchedule
/// previously called in a foreach loop.
/// </summary>
public class TrainingServiceTests : SqliteTestBase
{
    private TrainingService NewService() => new TrainingService(Factory);

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_NoMembershipsOrAssignments_ReturnsEmpty()
    {
        // No OrganizationMembership row, no Assignment row. The
        // volunteer isn't tied to any org or slot, so the service's
        // short-circuit returns an empty list before we even ask
        // SQLite for requirements. This guards the "you're not a
        // member of any organization yet" empty-state UI from
        // leaking cross-org requirements (a regression here would
        // surface as the aggregate dropping the membership-scoping
        // and trusting raw data instead).
        var user = TestData.Person(Factory, "Lonely", "Volunteer");

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_OrgRequirement_ReturnsIt()
    {
        // Basic happy path: user is in the org, an org-scoped
        // requirement exists, no completion → it appears.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        TestData.Requirement(Factory, content.Id, orgId: org.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        var req = Assert.Single(result);
        Assert.Equal(content.Id, req.TrainingContentId);
        Assert.Equal(org.Id, req.OrganizationId);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_OrgRequirement_WithValidCompletion_HidesIt()
    {
        // Cadence = Yearly → a completion with ExpiresUtc > now
        // satisfies the requirement; it must NOT appear in the
        // outstanding aggregate.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        TestData.Requirement(Factory, content.Id, orgId: org.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Completion(Factory, user.UserId, content.Id, DateTime.UtcNow);

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_OrgRequirement_WithExpiredCompletion_Reappears()
    {
        // The whole point of "Action needed": once a Yearly
        // completion rolls past ExpiresUtc, the requirement is in
        // the "Expired" history row AND in the outstanding list,
        // because the volunteer needs to retake it.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        TestData.Requirement(Factory, content.Id, orgId: org.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        // Two years ago → ExpiresUtc one year ago → expired.
        TestData.Completion(Factory, user.UserId, content.Id, DateTime.UtcNow.AddYears(-2));

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        var req = Assert.Single(result);
        Assert.Equal(content.Id, req.TrainingContentId);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_SlotRequirement_WithActiveAssignment_ShowsIt()
    {
        // Slot-scoped requirement: only shows when the user has a
        // non-cancelled Assignment on that slot. Since round N
        // TrainingContent is per-org, this is double-protected:
        // the slot's ministry's org owns the content AND the
        // user-scoping via OrganizationMembership gates the row.
        var org = TestData.Org(Factory, "Org A");
        var ministry = TestData.Ministry(Factory, org.Id, "Greeters");
        var slot = TestData.Slot(Factory, ministry.Id, "Welcome Desk");
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        TestData.Requirement(Factory, content.Id, slotId: slot.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        // Use future dates so the assignment doesn't accidentally
        // look "past" — the service doesn't filter on start time,
        // only on Status, but this keeps the seed honest.
        var start = DateTime.UtcNow.AddDays(7);
        TestData.Assignment(Factory, user.UserId, slot.Id, start, start.AddHours(2));

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        var req = Assert.Single(result);
        Assert.Equal(slot.Id, req.ServiceSlotId);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_SlotRequirement_NoActiveAssignment_DoesNotShow()
    {
        // Volunteering for Org A but NOT assigned to the slot →
        // slot-scoped requirement is hidden. The volunteer can't
        // be "outstanding" for training they don't need yet.
        var org = TestData.Org(Factory, "Org A");
        var ministry = TestData.Ministry(Factory, org.Id, "Greeters");
        var slot = TestData.Slot(Factory, ministry.Id, "Welcome Desk");
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        TestData.Requirement(Factory, content.Id, slotId: slot.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        // No Assignment row at all.

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_SlotRequirement_CancelledAssignment_DoesNotShow()
    {
        // Cancelled Assignment → slot-scoped requirement no longer
        // applies (mirrors the MySchedule "Org-scope" filter
        // excluding Cancelled rows).
        var org = TestData.Org(Factory, "Org A");
        var ministry = TestData.Ministry(Factory, org.Id, "Greeters");
        var slot = TestData.Slot(Factory, ministry.Id, "Welcome Desk");
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        TestData.Requirement(Factory, content.Id, slotId: slot.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        var start = DateTime.UtcNow.AddDays(7);
        TestData.Assignment(Factory, user.UserId, slot.Id, start, start.AddHours(2),
            AssignmentStatus.Cancelled);

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_MultipleOrgs_UnionOfOrgRequirements()
    {
        // Volunteer is in Org A AND Org B. Each org has its own
        // per-org training catalog with its own TrainingContent →
        // both org-scoped requirements must appear in the
        // aggregate. (Since round N, content A is owned by A and
        // content B by B, so this also implicitly verifies that
        // the per-org scoping doesn't block the outstanding-list
        // roll-up across the user's own memberships.)
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var contentA = TestData.TrainingContent(Factory, orgA.Id, "Safe Spaces");
        var contentB = TestData.TrainingContent(Factory, orgB.Id, "Concussion");
        TestData.Requirement(Factory, contentA.Id, orgId: orgA.Id);
        TestData.Requirement(Factory, contentB.Id, orgId: orgB.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, user.UserId, orgB.Id, OrganizationRole.Volunteer);

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.TrainingContentId == contentA.Id);
        Assert.Contains(result, r => r.TrainingContentId == contentB.Id);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_OrgRequirement_OutOfOrgScope_DoesNotShow()
    {
        // The hard privacy leak: a Requirement for Org B (with
        // content owned by Org B) should never reach a User who is
        // only in Org A. Since round N the TrainingContent also
        // belongs to Org B, this test catches both the membership
        // gate regression AND a regression that dropped the
        // per-org scoping on the content side.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var content = TestData.TrainingContent(Factory, orgB.Id, "Confidential B Training");
        TestData.Requirement(Factory, content.Id, orgId: orgB.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Volunteer);

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListMyHistoryAsync_NoCompletions_ReturnsEmpty()
    {
        var user = TestData.Person(Factory, "Vicky", "Volunteer");

        var result = await NewService().ListMyHistoryAsync(user.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListMyHistoryAsync_ReturnsNewestFirst()
    {
        // Three completions, distinct timestamps AND distinct
        // content versions (1, 2, 3) — the same user retaking the
        // same training as the content gets refreshed across years.
        // The schema's UNIQUE index on
        // (PersonUserId, TrainingContentId, TrainingContentVersion)
        // means we MUST pin distinct versions here; without that
        // any second insert for the same contentId would collide.
        // The service must sort them so the table renders
        // newest-at-top without the page having to re-sort.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        var t1 = DateTime.UtcNow.AddDays(-30);
        var t2 = DateTime.UtcNow.AddDays(-10);
        var t3 = DateTime.UtcNow.AddDays(-1);
        TestData.Completion(Factory, user.UserId, content.Id, t1, contentVersion: 1);
        TestData.Completion(Factory, user.UserId, content.Id, t2, contentVersion: 2);
        TestData.Completion(Factory, user.UserId, content.Id, t3, contentVersion: 3);

        var result = await NewService().ListMyHistoryAsync(user.UserId);

        Assert.Equal(3, result.Count);
        Assert.Equal(t3, result[0].CompletionUtc);
        Assert.Equal(t2, result[1].CompletionUtc);
        Assert.Equal(t1, result[2].CompletionUtc);
    }

    [Fact]
    public async Task ListMyHistoryAsync_EagerLoadsTrainingContent()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Completion(Factory, user.UserId, content.Id, DateTime.UtcNow);

        var result = await NewService().ListMyHistoryAsync(user.UserId);

        var row = Assert.Single(result);
        Assert.NotNull(row.TrainingContent);
        Assert.Equal("Safe Spaces", row.TrainingContent.Title);
    }

    [Fact]
    public async Task ListMyHistoryAsync_ExcludesOtherUsersCompletions()
    {
        // User A and User B each complete the same training — only
        // A's completions appear in A's history, only B's in B's.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var a = TestData.Person(Factory, "Alice", "Volunteer");
        var b = TestData.Person(Factory, "Bob", "Volunteer");
        TestData.Completion(Factory, a.UserId, content.Id, DateTime.UtcNow);
        TestData.Completion(Factory, b.UserId, content.Id, DateTime.UtcNow);

        var aHistory = await NewService().ListMyHistoryAsync(a.UserId);
        var bHistory = await NewService().ListMyHistoryAsync(b.UserId);

        Assert.Single(aHistory);
        Assert.Single(bHistory);
        Assert.Equal(a.UserId, aHistory[0].PersonUserId);
        Assert.Equal(b.UserId, bHistory[0].PersonUserId);
    }

    [Fact]
    public async Task FindMyOutstandingRequirementsAsync_OnlySlotAssignments_NoOrgMembership_ShowsSlotRequirement()
    {
        // Edge case: the user has NO OrganizationMembership at all
        // (maybe they were ungracefully demoted / their org closed)
        // but still holds a current, non-cancelled assignment on a
        // slot that has its own training. Per-org scoping (round N):
        // the TrainingContent here is owned by the SLOT's parent org.
        // The aggregate must still surface the slot-scoped requirement
        // via the Assignment path — we don't want them to silently lose
        // visibility into the training they actually need to take right
        // now. Pins the contract.
        var org = TestData.Org(Factory, "Org A");
        var ministry = TestData.Ministry(Factory, org.Id, "Greeters");
        var slot = TestData.Slot(Factory, ministry.Id, "Welcome Desk");
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        TestData.Requirement(Factory, content.Id, slotId: slot.Id);
        var user = TestData.Person(Factory, "Nomad", "Volunteer");
        // Intentionally NO Membership() call for Org A.
        var start = DateTime.UtcNow.AddDays(3);
        TestData.Assignment(Factory, user.UserId, slot.Id, start, start.AddHours(2));

        var result = await NewService().FindMyOutstandingRequirementsAsync(user.UserId);

        var req = Assert.Single(result);
        Assert.Equal(content.Id, req.TrainingContentId);
        Assert.Equal(slot.Id, req.ServiceSlotId);
    }

    [Fact]
    public async Task FindOutstandingRequirementsAsync_Overload_StillWorks_ForBackwardCompat()
    {
        // The single-org overload is still called by code paths we
        // didn't audit yet (regression guard). Validates the
        // Yearly + completion-vs-now math hasn't drifted.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        TestData.Requirement(Factory, content.Id, orgId: org.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Completion(Factory, user.UserId, content.Id, DateTime.UtcNow);

        var result = await NewService().FindOutstandingRequirementsAsync(user.UserId, org.Id);

        Assert.Empty(result);
    }

    // ─── RecordCompletionAsync (round N per-org gate) ───────────────────────────

    [Fact]
    public async Task RecordCompletionAsync_CallerMemberOfContentOrg_ReturnsRecorded()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        TestData.Requirement(Factory, content.Id, orgId: org.Id);
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        // Round M (engagement gate): seed watch progress so the
        // Video rule's 95%-of-ActualDuration + 30s absolute floor are
        // both satisfied. Without this seed the rule refuses with
        // InsufficientEngagement (correct production behavior) and the
        // legacy assertion breaks.
        var svc = NewService();
        await svc.SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 60, ActualDurationSec = 60 });

        var result = await svc.RecordCompletionAsync(user.UserId, content.Id, DateTime.UtcNow);

        Assert.Equal(TrainingCompletionResult.Recorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.TrainingCompletions.AnyAsync(c =>
            c.PersonUserId == user.UserId && c.TrainingContentId == content.Id));
    }

    [Fact]
    public async Task RecordCompletionAsync_CallerNotInContentOrg_ReturnsNotInOrg()
    {
        // User is in Org A but the training content is owned by Org B.
        // Per-org scoping (round N): the service refuses; no row inserted.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var content = TestData.TrainingContent(Factory, orgB.Id, "Confidential B Training");
        var user = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Volunteer);

        var result = await NewService().RecordCompletionAsync(user.UserId, content.Id, DateTime.UtcNow);

        Assert.Equal(TrainingCompletionResult.NotInOrg, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.TrainingCompletions.AnyAsync(c =>
            c.PersonUserId == user.UserId && c.TrainingContentId == content.Id));
    }

    [Fact]
    public async Task RecordCompletionAsync_UnknownContentId_ReturnsContentNotFound()
    {
        var user = TestData.Person(Factory, "Vicky", "Volunteer");

        var result = await NewService().RecordCompletionAsync(user.UserId, 999_999, DateTime.UtcNow);

        Assert.Equal(TrainingCompletionResult.ContentNotFound, result);
    }

    [Fact]
    public async Task RecordCompletionAsync_AdminOfContentOrg_Allowed()
    {
        // Admins are members too — gate is on OrganizationMembership
        // presence, not on role. Locks in the role-agnostic invariant
        // a future refactor might accidentally tighten.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Admin Onboarding");
        var admin = TestData.Person(Factory, "Alex", "Admin");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        // Round M engagement gate — same seed as the volunteer happy path.
        var svc = NewService();
        await svc.SyncActivityAsync(admin.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 60, ActualDurationSec = 60 });

        var result = await svc.RecordCompletionAsync(admin.UserId, content.Id, DateTime.UtcNow);

        Assert.Equal(TrainingCompletionResult.Recorded, result);
    }

    [Fact]
    public async Task RecordCompletionAsync_CoordinatorOfContentOrg_Allowed()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Coord Onboarding");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);

        // Round M engagement gate.
        var svc = NewService();
        await svc.SyncActivityAsync(coord.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 60, ActualDurationSec = 60 });

        var result = await svc.RecordCompletionAsync(coord.UserId, content.Id, DateTime.UtcNow);

        Assert.Equal(TrainingCompletionResult.Recorded, result);
    }

    [Fact]
    public async Task RecordCompletionAsync_EmptyPersonId_ReturnsNotInOrg()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");

        var result = await NewService().RecordCompletionAsync("", content.Id, DateTime.UtcNow);

        Assert.Equal(TrainingCompletionResult.NotInOrg, result);
    }

    // ─── ListOrgTrainingAsync (per-org catalog filter) ─────────────────────────

    [Fact]
    public async Task ListOrgTrainingAsync_OnlyReturnsRequestedOrgsContent()
    {
        // Two orgs each with their own per-org content. Caller asks for
        // Org A's catalog → only A's content comes back.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var contentA = TestData.TrainingContent(Factory, orgA.Id, "Safe Spaces A");
        TestData.TrainingContent(Factory, orgB.Id, "Safe Spaces B");

        var result = await NewService().ListOrgTrainingAsync(orgA.Id);

        var row = Assert.Single(result);
        Assert.Equal(orgA.Id, row.OrganizationId);
        Assert.Equal(contentA.Id, row.Id);
    }

    [Fact]
    public async Task ListOrgTrainingAsync_EmptyOrg_ReturnsEmpty()
    {
        var org = TestData.Org(Factory, "Empty Org");

        var result = await NewService().ListOrgTrainingAsync(org.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListOrgTrainingAsync_MultipleContentInOrg_OrdersByTitle()
    {
        var org = TestData.Org(Factory, "Org A");
        TestData.TrainingContent(Factory, org.Id, "Zeta");
        TestData.TrainingContent(Factory, org.Id, "Alpha");
        TestData.TrainingContent(Factory, org.Id, "Mu");

        var result = await NewService().ListOrgTrainingAsync(org.Id);

        Assert.Equal(new[] { "Alpha", "Mu", "Zeta" }, result.Select(c => c.Title));
    }

    // ─── ListSlotOrgTrainingAsync (slot's parent org catalog) ──────────────────

    [Fact]
    public async Task ListSlotOrgTrainingAsync_ReturnsSlotParentOrgsContent()
    {
        var org = TestData.Org(Factory, "Org A");
        var ministry = TestData.Ministry(Factory, org.Id, "Greeters");
        var slot = TestData.Slot(Factory, ministry.Id, "Welcome Desk");
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");

        var result = await NewService().ListSlotOrgTrainingAsync(slot.Id);

        var row = Assert.Single(result);
        Assert.Equal(content.Id, row.Id);
        Assert.Equal(org.Id, row.OrganizationId);
    }

    [Fact]
    public async Task ListSlotOrgTrainingAsync_ExcludesForeignOrgsContent()
    {
        // Slot belongs to Org A. Org B has its own TrainingContent that
        // should NOT appear in the slot dropdown — otherwise slots could
        // attach to other orgs' training.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var ministryA = TestData.Ministry(Factory, orgA.Id, "Greeters");
        var slotA = TestData.Slot(Factory, ministryA.Id, "Welcome Desk");
        TestData.TrainingContent(Factory, orgA.Id, "Local A training");
        TestData.TrainingContent(Factory, orgB.Id, "Foreign B training");

        var result = await NewService().ListSlotOrgTrainingAsync(slotA.Id);

        var row = Assert.Single(result);
        Assert.Equal(orgA.Id, row.OrganizationId);
    }

    [Fact]
    public async Task ListSlotOrgTrainingAsync_UnknownSlot_ReturnsEmpty()
    {
        var result = await NewService().ListSlotOrgTrainingAsync(999_999);

        Assert.Empty(result);
    }

    // ─── ListManageableTrainingAsync (admin union) ─────────────────────────────

    [Fact]
    public async Task ListManageableTrainingAsync_AdminOfOneOrg_ReturnsOnlyThatOrgsContent()
    {
        var admin = TestData.Person(Factory, "Alex", "Admin");
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        TestData.Membership(Factory, admin.UserId, orgA.Id, OrganizationRole.Admin);
        TestData.TrainingContent(Factory, orgA.Id, "A training");
        TestData.TrainingContent(Factory, orgB.Id, "B training");

        var result = await NewService().ListManageableTrainingAsync(admin.UserId);

        var row = Assert.Single(result);
        Assert.Equal(orgA.Id, row.OrganizationId);
    }

    [Fact]
    public async Task ListManageableTrainingAsync_AdminOfMultipleOrgs_ReturnsUnion()
    {
        var admin = TestData.Person(Factory, "Alex", "Multi");
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        TestData.Membership(Factory, admin.UserId, orgA.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, admin.UserId, orgB.Id, OrganizationRole.Admin);
        var a1 = TestData.TrainingContent(Factory, orgA.Id, "A training");
        var b1 = TestData.TrainingContent(Factory, orgB.Id, "B training");

        var result = await NewService().ListManageableTrainingAsync(admin.UserId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == a1.Id);
        Assert.Contains(result, c => c.Id == b1.Id);
    }

    [Fact]
    public async Task ListManageableTrainingAsync_NotAdminOfAnyOrg_ReturnsEmpty()
    {
        // Admin of zero orgs → empty catalog. A user who is Admin of no orgs
        // shouldn't see anything on the Manage page.
        var volunteer = TestData.Person(Factory, "Vee", "Volunteer");
        var org = TestData.Org(Factory, "Org A");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.TrainingContent(Factory, org.Id, "Org A training");

        var result = await NewService().ListManageableTrainingAsync(volunteer.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListManageableTrainingAsync_CoordinatorNotAdmin_ReturnsEmpty()
    {
        // Coordinator (but not Admin) of org A → empty. The manage page
        // is Admin-only; Coordinator should not see the union.
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var org = TestData.Org(Factory, "Org A");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.TrainingContent(Factory, org.Id, "Org A training");

        var result = await NewService().ListManageableTrainingAsync(coord.UserId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListManageableTrainingAsync_EagerLoadedOrganization_NonNull()
    {
        // The page header renders "Admin of: <Org Name>"; the service must
        // eager-load the Organization nav property so the .Name works
        // without an N+1.
        var admin = TestData.Person(Factory, "Alex", "Admin");
        var org = TestData.Org(Factory, "Org A");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.TrainingContent(Factory, org.Id, "A training");

        var result = await NewService().ListManageableTrainingAsync(admin.UserId);

        var row = Assert.Single(result);
        Assert.NotNull(row.Organization);
        Assert.Equal("Org A", row.Organization!.Name);
    }

    // ─── Round-FR-2.2: MarkSingleCompleteAsync (manual-mark audit path) ─────────
    // Bypasses the engagement-eligibility gate (decision Q6) but requires
    // non-empty notes (decision Q5) and Admin/Coordinator permission.

    [Fact]
    public async Task MarkSingleCompleteAsync_EmptyNotes_ReturnsManualMarkNotesRequired()
    {
        // Decision Q5: empty / whitespace-only notes refused. The audit
        // trail distinction (auto-engaged vs coord-manual) only carries
        // weight if every manual mark has a non-empty reason.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "");

        Assert.Equal(TrainingCompletionResult.ManualMarkNotesRequired, result);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_WhitespaceNotes_ReturnsManualMarkNotesRequired()
    {
        // Whitespace-only notes ARE empty for the purposes of decision
        // Q5. The validate-on-write pattern keeps the column meaningful
        // even if some future caller forgets the trim.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "   \t\n  ");

        Assert.Equal(TrainingCompletionResult.ManualMarkNotesRequired, result);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_UnknownContent_ReturnsContentNotFound()
    {
        var coord = TestData.Person(Factory, "Chris", "Coord");

        var result = await NewService().MarkSingleCompleteAsync(
            999_999, "any-user-id", coord.UserId, "any reason");

        Assert.Equal(TrainingCompletionResult.ContentNotFound, result);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_CallerNotAdminOrCoordinator_ReturnsManualMarkPermissionDenied()
    {
        // Caller is a plain Volunteer of the content's org — no role
        // authority to assert competence out-of-band; refused.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        var otherVolunteer = TestData.Person(Factory, "Oscar", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, otherVolunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, otherVolunteer.UserId, "I vouch for them");

        Assert.Equal(TrainingCompletionResult.ManualMarkPermissionDenied, result);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_CallerAdminOfOtherOrg_ReturnsManualMarkPermissionDenied()
    {
        // Cross-org authority doesn't transfer. An Admin of Org B can't
        // mark training owned by Org A; the audit trail said "admin
        // asserted competence" — a foreign-org admin doesn't have that
        // authority.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var contentA = TestData.TrainingContent(Factory, orgA.Id, "Safe Spaces");
        var adminB = TestData.Person(Factory, "Ben", "AdminB");
        var volunteerA = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, adminB.UserId, orgB.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, volunteerA.UserId, orgA.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            contentA.Id, volunteerA.UserId, adminB.UserId, "Cross-org vouch");

        Assert.Equal(TrainingCompletionResult.ManualMarkPermissionDenied, result);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_VolunteerNotInOrg_ReturnsNotInOrg()
    {
        // The volunteer being marked must actually be a member of the
        // content's org — otherwise we'd be writing a training record
        // for a stranger who never joined. Mirrors RecordCompletionAsync.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var contentA = TestData.TrainingContent(Factory, orgA.Id, "Safe Spaces");
        var coordA = TestData.Person(Factory, "Chris", "Coord");
        var stranger = TestData.Person(Factory, "Stranger", "McNobody");
        TestData.Membership(Factory, coordA.UserId, orgA.Id, OrganizationRole.MinistryDirector);
        // Intentionally NO Membership for stranger.

        var result = await NewService().MarkSingleCompleteAsync(
            contentA.Id, stranger.UserId, coordA.UserId, "I vouch for them");

        Assert.Equal(TrainingCompletionResult.NotInOrg, result);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_HappyPath_Coordinator_RecordsWithAuditFields()
    {
        // Decision Q6: bypasses the engagement gate. No SyncActivityAsync
        // call → ordinarily would return InsufficientEngagement, but the
        // coordinator's manual path asserts competence out of band.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "Walked Vicky through the material in person 2026-07-06.");

        Assert.Equal(TrainingCompletionResult.ManualMarkRecorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        // Audit-trail triple: source + marker + notes.
        Assert.Equal(TrainingCompletionSource.CoordinatorManualSingle, completion.CompletionSource);
        Assert.Equal(coord.UserId, completion.MarkedCompleteByUserId);
        Assert.Equal("Walked Vicky through the material in person 2026-07-06.", completion.ManualCompletionNotes);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_HappyPath_Admin_RecordsWithAuditFields()
    {
        // Same contract for Admin role — pin that the role-arbitrary
        // authority works for Admins too, not just Coordinators.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safety Drill");
        var admin = TestData.Person(Factory, "Alex", "Admin");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, admin.UserId, "Admin override: veteran firefighter");

        Assert.Equal(TrainingCompletionResult.ManualMarkRecorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        Assert.Equal(TrainingCompletionSource.CoordinatorManualSingle, completion.CompletionSource);
        Assert.Equal(admin.UserId, completion.MarkedCompleteByUserId);
        Assert.Equal("Admin override: veteran firefighter", completion.ManualCompletionNotes);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_ExistingCompletion_OverwritesInPlace()
    {
        // Decision Q7: latest-wins. The audit trail captures who marked
        // most recently AND what they wrote. Earlier record (UserOnline
        // for instance) is replaced wholesale by the manual mark.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        // Seed an existing completion that would normally be user-online.
        TestData.Completion(Factory, volunteer.UserId, content.Id, DateTime.UtcNow);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "Reaffirming after audit.");

        Assert.Equal(TrainingCompletionResult.ManualMarkRecorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completions = await db.TrainingCompletions
            .Where(c => c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id)
            .ToListAsync();
        // Still ONE row — latest-wins means in-place overwrite, not append.
        Assert.Single(completions);
        Assert.Equal(TrainingCompletionSource.CoordinatorManualSingle, completions[0].CompletionSource);
        Assert.Equal(coord.UserId, completions[0].MarkedCompleteByUserId);
        Assert.Equal("Reaffirming after audit.", completions[0].ManualCompletionNotes);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_YearlyCadenceExpiryDerived()
    {
        // Cadence=Yearly → ExpiresUtc is CompletionUtc + 1 year. Locks
        // in the requirement-derived expiry math mirror so the manual
        // path and the engagement-verified path age completion rows
        // identically.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "Manual mark for annual training.");

        Assert.Equal(TrainingCompletionResult.ManualMarkRecorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        Assert.NotNull(completion.ExpiresUtc);
        // Yearly cadence + completion has no other anchor; we just need
        // the expiry to be in the future and roughly a year out.
        Assert.True(completion.ExpiresUtc > DateTime.UtcNow.AddDays(360));
        Assert.True(completion.ExpiresUtc < DateTime.UtcNow.AddDays(367));
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_OneTimeCadenceExpiryIsNull()
    {
        // OneTime cadence → completion lives forever (ExpiresUtc = null),
        // so the volunteer's "completed" status never rolls off. Locks
        // in the cadence-branch behavior so a future enum-add won't
        // silently default to Yearly here.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "One-time onboarding");
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: TrainingCadence.OneTime);
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "Onboarding completed manually.");

        Assert.Equal(TrainingCompletionResult.ManualMarkRecorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        Assert.Null(completion.ExpiresUtc);
    }

    [Fact]
    public async Task MarkSingleCompleteAsync_BypassesEngagementGate()
    {
        // Decision Q6: explicit contract — a coarse proof that the
        // volunteer hasn't engaged, but the manual mark still succeeds.
        // Without SyncActivityAsync: HighestWatchedSec=0, ViewedPages=[],
        // Activity row absent → RecordCompletionAsync would return
        // InsufficientEngagement; MarkSingleCompleteAsync must NOT.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        // NO SyncActivityAsync call.

        var result = await NewService().MarkSingleCompleteAsync(
            content.Id, volunteer.UserId, coord.UserId, "Bypass contract: out-of-band competence.");

        Assert.Equal(TrainingCompletionResult.ManualMarkRecorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        // No activity row was added by the manual path — confirms the
        // service didn't silently downgrade to user-engagement mode.
        Assert.False(await db.TrainingActivities.AnyAsync(a =>
            a.PersonUserId == volunteer.UserId && a.TrainingContentId == content.Id));
    }
}
