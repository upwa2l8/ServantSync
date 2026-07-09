using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Round-FR-6 service-layer tests pinning the WHO (RBAC), WHAT (cadence
/// × completion-state matrix), and HOW (filter chips + sort toggle +
/// counts widget) of the per-org training-due-soon grid. Detail-page
/// + page-access tests land in separate files when Razor layer ships.
/// </summary>
public class TrainingDueSoonServiceTests : SqliteTestBase
{
    private TrainingDueSoonService NewService() => new TrainingDueSoonService(Factory);

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime Window30 = Now.AddDays(30);

    // ───────────────────────────── RBAC tests ─────────────────────────────

    [Fact]
    public async Task ListAtRisk_AdminInOrg_CanRead()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task ListAtRisk_MinistryDirectorInOrg_CanRead()
    {
        var org = TestData.Org(Factory);
        var md = TestData.Person(Factory, "MinDir", "User");
        TestData.Membership(Factory, md.UserId, org.Id, OrganizationRole.MinistryDirector);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, md.UserId);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task ListAtRiskCounts_MinistryDirectorInOrg_CanRead()
    {
        var org = TestData.Org(Factory);
        var md = TestData.Person(Factory, "MinDir", "User");
        TestData.Membership(Factory, md.UserId, org.Id, OrganizationRole.MinistryDirector);

        var service = NewService();
        var counts = await service.ListAtRiskCountsAsync(org.Id, md.UserId);
        Assert.NotNull(counts);
        Assert.Equal(0, counts.OverdueCount);
        Assert.Equal(0, counts.DueSoonCount);
    }

    [Fact]
    public async Task ListAtRisk_VolunteerInOrg_ThrowsUnauthorized()
    {
        var org = TestData.Org(Factory);
        var vol = TestData.Person(Factory, "Vol", "User");
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        var service = NewService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, vol.UserId));
    }

    [Fact]
    public async Task ListAtRisk_SlotCoordinatorInOrg_ThrowsUnauthorized()
    {
        // Per FR-6 RBAC matrix: Slot Coordinator denied (slot scope is
        // too narrow for "what training is due across my slots" to be
        // a meaningful query).
        var org = TestData.Org(Factory);
        var sc = TestData.Person(Factory, "SC", "User");
        TestData.Membership(Factory, sc.UserId, org.Id, OrganizationRole.SlotCoordinator);

        var service = NewService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, sc.UserId));
    }

    [Fact]
    public async Task ListAtRisk_ForeignOrgAdmin_ThrowsUnauthorized()
    {
        var own = TestData.Org(Factory, "Own Org");
        var foreign = TestData.Org(Factory, "Foreign Org");
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, foreign.Id, OrganizationRole.Admin);

        var service = NewService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListAtRiskAsync(own.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId));
    }

    [Fact]
    public async Task ListAtRisk_EmptyCallerUserId_ThrowsUnauthorized()
    {
        var org = TestData.Org(Factory);
        var service = NewService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, ""));
    }

    [Fact]
    public async Task ListAtRisk_PersonWithNoMembership_ThrowsUnauthorized()
    {
        var org = TestData.Org(Factory);
        var service = NewService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, "completely-unknown-user-id"));
    }

    // ─────────────── Cadence x Completion-state matrix tests ───────────────

    [Fact]
    public async Task Yearly_NoCompletion_RowIsOverdue()
    {
        var (admin, _) = OrgWithAdminAndRequirement(cadence: TrainingCadence.Yearly);
        var service = NewService();

        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Single(rows);
        Assert.Equal(TrainingDueSoonStatus.Overdue, rows[0].Status);
        Assert.Null(rows[0].LastCompletionUtc);
        Assert.Null(rows[0].ExpiresUtc);
        Assert.Null(rows[0].DaysDelta);   // null because no ExpiresUtc to delta against
    }

    [Fact]
    public async Task Yearly_ExpiredCompletion_RowIsOverdue()
    {
        var (admin, person) = OrgWithAdminAndRequirement(cadence: TrainingCadence.Yearly);
        // Completion 1 year + 5 days ago → ExpiresUtc = ~5 days in the past.
        // -370 is robust against leap-year arithmetic where AddYears(1)
        // can collapse to exactly Now (rounding 0 → not < 0).
        var past = Now.AddDays(-370);
        TestData.Completion(Factory, person.UserId, admin.ContentId, past);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Single(rows);
        Assert.Equal(TrainingDueSoonStatus.Overdue, rows[0].Status);
        Assert.NotNull(rows[0].LastCompletionUtc);
        Assert.NotNull(rows[0].ExpiresUtc);
        Assert.True(rows[0].DaysDelta! < 0);   // negative = overdue
    }

    [Fact]
    public async Task Yearly_ExpiresIn29Days_RowIsDueSoon()
    {
        var (admin, person) = OrgWithAdminAndRequirement(cadence: TrainingCadence.Yearly);
        // 336 days ago → ExpiresUtc = today + 29 days → DueSoon.
        TestData.Completion(Factory, person.UserId, admin.ContentId, Now.AddDays(-336));

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Single(rows);
        Assert.Equal(TrainingDueSoonStatus.DueSoon, rows[0].Status);
        Assert.InRange(rows[0].DaysDelta!.Value, 1, 30);
    }

    [Fact]
    public async Task Yearly_ExpiresIn31Days_CompliantFilteredOut()
    {
        // Boundary test: 30 = DueSoon (inclusive), 31 = Compliant (filtered).
        var (admin, person) = OrgWithAdminAndRequirement(cadence: TrainingCadence.Yearly);
        // 334 days ago → ExpiresUtc = today + 31 days → Compliant.
        TestData.Completion(Factory, person.UserId, admin.ContentId, Now.AddDays(-334));

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Empty(rows);   // Compliant excluded by AllAtRisk filter
    }

    [Fact]
    public async Task Yearly_CompletedYesterday_FilteredOutOfAllAtRisk()
    {
        // A Yearly completion completed yesterday still has ~364 more
        // days left → Compliant → AllAtRisk filter excludes.
        var (admin, person) = OrgWithAdminAndRequirement(cadence: TrainingCadence.Yearly);
        TestData.Completion(Factory, person.UserId, admin.ContentId, Now.AddDays(-1));

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task OneTime_NoCompletion_NotRequired_ExcludedFromAllAtRisk()
    {
        // Spec decision Q5: OneTime never-tracked = NotRequired (carved
        // out of at-risk).
        var (admin, _) = OrgWithAdminAndRequirement(cadence: TrainingCadence.OneTime);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Empty(rows);
        var counts = await service.ListAtRiskCountsAsync(admin.OrgId, admin.UserId);
        Assert.Equal(0, counts.TotalAtRiskCount);
    }

    [Fact]
    public async Task OneTime_Completed_ForeverValid_NoRow()
    {
        // ExpiresUtc null on completion with OneTime → forever valid →
        // Compliant → excluded from AllAtRisk filter.
        var (admin, person) = OrgWithAdminAndRequirement(cadence: TrainingCadence.OneTime);
        var completion = TestData.Completion(Factory, person.UserId, admin.ContentId, Now.AddDays(-30));
        // Wipe ExpiresUtc (TestData.Completion hardcodes +1 year; one
        // explicit override makes the OneTime semantics testable).
        await using (var db = Factory.CreateDbContext())
        {
            db.TrainingCompletions.Attach(completion);
            completion.ExpiresUtc = null;
            await db.SaveChangesAsync();
        }

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task EveryMonths_NearExpiry_RowIsDueSoon()
    {
        var (admin, person) = OrgWithAdminAndRequirement(
            cadence: TrainingCadence.EveryMonths, cadenceMonths: 6);
        // TestData.Completion hardcodes ExpiresUtc = CompletionUtc + 1year
        // (cadence-independent). Pick CompletionUtc = Now - 336d →
        // ExpiresUtc = +29d → DueSoon window. Avoids the manual override path.
        TestData.Completion(Factory, person.UserId, admin.ContentId, Now.AddDays(-336));

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Single(rows);
        Assert.Equal(TrainingDueSoonStatus.DueSoon, rows[0].Status);
        Assert.InRange(rows[0].DaysDelta!.Value, 25, 30);
    }

    // ───────────────────── Multi-row + stub inclusion ─────────────────────

    [Fact]
    public async Task MultipleRequirements_PersonAppearsMultipleTimes()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var c1 = TestData.TrainingContent(Factory, org.Id, "Safety");
        var c2 = TestData.TrainingContent(Factory, org.Id, "Comms");
        TestData.Requirement(Factory, c1.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);
        TestData.Requirement(Factory, c2.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.TrainingContentId == c1.Id);
        Assert.Contains(rows, r => r.TrainingContentId == c2.Id);
    }

    [Fact]
    public async Task StubPerson_AppearsAsAtRiskRowWithIsStub()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        // Create a stub-mode person (round-FR-3 stub Person manually
        // flipped to IsStub=true after the helper saves it).
        var stub = TestData.Person(Factory, "Stubby", "Stub-person");
        stub.IsStub = true;
        await using (var db = Factory.CreateDbContext())
        {
            db.People.Attach(stub);
            db.Entry(stub).Property(p => p.IsStub).IsModified = true;
            await db.SaveChangesAsync();
        }
        TestData.Membership(Factory, stub.UserId, org.Id, OrganizationRole.Volunteer);

        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);
        // Stub has no completion → Overdue.

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        var stubRow = Assert.Single(rows, r => r.PersonUserId == stub.UserId);
        Assert.True(stubRow.IsStub);
    }

    // ─────────────────────────── Filter chip routing ───────────────────────────

    [Fact]
    public async Task Filter_OverdueOnly_ExcludesDueSoon()
    {
        // Two people, both with same Yearly requirement, no completions.
        // Both Overdue; OverdueOnly filter returns 2; DueIn30Days returns 0.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        var p1 = TestData.Person(Factory, "First", "Person");
        TestData.Membership(Factory, p1.UserId, org.Id, OrganizationRole.Volunteer);
        var p2 = TestData.Person(Factory, "Second", "Person");
        TestData.Membership(Factory, p2.UserId, org.Id, OrganizationRole.Volunteer);
        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);

        var service = NewService();
        var overdueOnly = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.OverdueOnly, TrainingDueSoonSort.ByUrgency, admin.UserId);
        var dueIn30 = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.DueIn30Days, TrainingDueSoonSort.ByUrgency, admin.UserId);

        Assert.Equal(3, overdueOnly.Count);    // admin + p1 + p2, all Yearly-no-completion
        Assert.Empty(dueIn30);
        Assert.All(overdueOnly, r => Assert.Equal(TrainingDueSoonStatus.Overdue, r.Status));
    }

    [Fact]
    public async Task Filter_CompletedRecently_ShowsRowsWithCompletionInLast30Days()
    {
        var (admin, person) = OrgWithAdminAndRequirement(cadence: TrainingCadence.Yearly);
        // 5 days ago → still has ~360 days left; Compliant now, but
        // CompletedRecently filter should pick it up because LastCompletionUtc
        // is within 30 days.
        TestData.Completion(Factory, person.UserId, admin.ContentId, Now.AddDays(-5));

        var service = NewService();
        var rows = await service.ListAtRiskAsync(admin.OrgId, TrainingDueSoonFilter.CompletedRecently, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Single(rows);
        Assert.NotNull(rows[0].LastCompletionUtc);
    }

    // ───────────────────────────── Sort toggle ─────────────────────────────

    [Fact]
    public async Task Sort_ByUrgency_OverdueFirst_ThenDueSoon()
    {
        // One person with two overdue completions + one person with a
        // DueSoon row. Sort by urgency = overdue rows before DueSoon.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var overduePerson = TestData.Person(Factory, "Overdue", "Person");
        TestData.Membership(Factory, overduePerson.UserId, org.Id, OrganizationRole.Volunteer);

        var dueSoonPerson = TestData.Person(Factory, "DueSoon", "Person");
        TestData.Membership(Factory, dueSoonPerson.UserId, org.Id, OrganizationRole.Volunteer);

        var c1 = TestData.TrainingContent(Factory, org.Id, "Older");
        var c2 = TestData.TrainingContent(Factory, org.Id, "Newer");
        TestData.Requirement(Factory, c1.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);
        TestData.Requirement(Factory, c2.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);

        // Overdue for the overduePerson on both: very overdue + slightly overdue
        TestData.Completion(Factory, overduePerson.UserId, c1.Id, Now.AddDays(-400));  // 35 overdue
        TestData.Completion(Factory, overduePerson.UserId, c2.Id, Now.AddDays(-370));  // 5 overdue

        // DueSoon for the dueSoonPerson
        TestData.Completion(Factory, dueSoonPerson.UserId, c1.Id, Now.AddDays(-345));  // ~20 ahead

        // Silence the 3 unused cross-product combinations so they remain
        // Compliant and don't pollute the at-risk sort:
        TestData.Completion(Factory, admin.UserId, c1.Id, Now.AddDays(-50));
        TestData.Completion(Factory, admin.UserId, c2.Id, Now.AddDays(-50));
        TestData.Completion(Factory, dueSoonPerson.UserId, c2.Id, Now.AddDays(-50));

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Equal(3, rows.Count);
        Assert.All(rows.Take(2), r => Assert.Equal(TrainingDueSoonStatus.Overdue, r.Status));
        Assert.Equal(TrainingDueSoonStatus.DueSoon, rows[2].Status);
    }

    [Fact]
    public async Task Sort_ByPersonName_Alphabetical()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        var zUser = TestData.Person(Factory, "Aaron", "Zucker");
        TestData.Membership(Factory, zUser.UserId, org.Id, OrganizationRole.Volunteer);
        var aUser = TestData.Person(Factory, "Bea", "Yang");
        TestData.Membership(Factory, aUser.UserId, org.Id, OrganizationRole.Volunteer);

        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByPersonName, admin.UserId);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Aaron Zucker", rows[0].PersonDisplayName);
        Assert.Equal("Admin User", rows[1].PersonDisplayName);
        Assert.Equal("Bea Yang", rows[2].PersonDisplayName);
    }

    // ──────────────────────────── Counts widget ────────────────────────────

    [Fact]
    public async Task Counts_OneOverdueOneDueSoon_ReturnsCorrectTotals()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var overdueP = TestData.Person(Factory, "Overdue", "Person");
        TestData.Membership(Factory, overdueP.UserId, org.Id, OrganizationRole.Volunteer);
        var dueSoonP = TestData.Person(Factory, "DueSoon", "Person");
        TestData.Membership(Factory, dueSoonP.UserId, org.Id, OrganizationRole.Volunteer);

        var overdueContent = TestData.TrainingContent(Factory, org.Id, "Safety");
        var dueSoonContent = TestData.TrainingContent(Factory, org.Id, "Comms");
        TestData.Requirement(Factory, overdueContent.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);
        TestData.Requirement(Factory, dueSoonContent.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);

        TestData.Completion(Factory, overdueP.UserId, overdueContent.Id, Now.AddDays(-400));  // overdue
        TestData.Completion(Factory, dueSoonP.UserId, dueSoonContent.Id, Now.AddDays(-336));  // DueSoon

        // Silence the 4 unused cross-product combinations so the counts
        // widget reads exactly the per-org completions matrix (1 over + 1 due).
        TestData.Completion(Factory, admin.UserId, overdueContent.Id, Now.AddDays(-50));
        TestData.Completion(Factory, admin.UserId, dueSoonContent.Id, Now.AddDays(-50));
        TestData.Completion(Factory, overdueP.UserId, dueSoonContent.Id, Now.AddDays(-50));
        TestData.Completion(Factory, dueSoonP.UserId, overdueContent.Id, Now.AddDays(-50));

        var service = NewService();
        var counts = await service.ListAtRiskCountsAsync(org.Id, admin.UserId);
        Assert.Equal(1, counts.OverdueCount);
        Assert.Equal(1, counts.DueSoonCount);
        Assert.Equal(2, counts.TotalAtRiskCount);
    }

    [Fact]
    public async Task Counts_NoData_ReturnsZero()
    {
        var admin = TestData.Person(Factory, "Admin", "User");
        var org = TestData.Org(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var service = NewService();
        var counts = await service.ListAtRiskCountsAsync(org.Id, admin.UserId);
        Assert.Equal(0, counts.OverdueCount);
        Assert.Equal(0, counts.DueSoonCount);
    }

    // ──────────────────────────── Slot-scoped ────────────────────────────

    [Fact]
    public async Task SlotScopedRequirement_RendersSlotNameInScope()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        var ministry = TestData.Ministry(Factory, org.Id, "Greeters");
        var slot = TestData.Slot(Factory, ministry.Id, "Sunday Welcome Desk");
        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, slotId: slot.Id, cadence: TrainingCadence.Yearly);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Single(rows);            Assert.Equal(slot.Id, rows[0].SlotId);
            Assert.Equal("Sunday Welcome Desk", rows[0].SlotName);
            Assert.Equal("Slot · Sunday Welcome Desk", rows[0].RequirementScope);
            // Round-FR-6 Razor layer: Razor needs MinistryId to build the
            // /Organizations/{OrgId}/Ministries/{MinistryId}/Roles/{SlotId}
            // deep-link. This pin catches a future regression that drops
            // the capture from the service impl.
            Assert.Equal(ministry.Id, rows[0].MinistryId);
    }

    // ──────────────────────────── Edge / empty ────────────────────────────

    [Fact]
    public async Task OrgWithNoRequirements_ReturnsEmpty()
    {
        var admin = TestData.Person(Factory, "Admin", "User");
        var org = TestData.Org(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var service = NewService();
        var rows = await service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task OrgWithRequirementsButNoMembers_ReturnsEmpty()
    {
        var org = TestData.Org(Factory);
        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: TrainingCadence.Yearly);

        // No membership in this org; the admin caller is in a different org (RBAC blocks anyway).
        var otherOrg = TestData.Org(Factory, "Other");
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, otherOrg.Id, OrganizationRole.Admin);

        var service = NewService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListAtRiskAsync(org.Id, TrainingDueSoonFilter.AllAtRisk, TrainingDueSoonSort.ByUrgency, admin.UserId));
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    /// <summary>
    /// Convenience builder: creates a Test Org with one Yearly/OneTime/EveryMonths
    /// requirement owned by a TrainingContent and one Admin-Member caller + one
    /// Volunteer-Member volunteer. Returns the (admin, volunteer, contentId)
    /// triangle the test mutates with a completion. Used by ~all of the
    /// cadence × completion-state matrix tests so they don't repeat the
    /// setup boilerplate.
    /// </summary>
    private (OrgFixture Org, Person Volunteer) OrgWithAdminAndRequirement(
        TrainingCadence cadence, int? cadenceMonths = null)
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory, "Admin", "User");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var vol = TestData.Person(Factory, "Volunteer", "Person");
        TestData.Membership(Factory, vol.UserId, org.Id, OrganizationRole.Volunteer);

        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id, cadence: cadence, cadenceMonths: cadenceMonths);

        // Silence the admin so they don't pollute the at-risk cross-product:
        // a 50-day-old completion has ExpiresUtc ~315 days ahead → Compliant
        // → filtered out of AllAtRisk + DueIn30Days + OverdueOnly chips.
        // Also excluded from CompletedRecently (LastCompletionUtc > 30 days old).
        TestData.Completion(Factory, admin.UserId, content.Id, DateTime.UtcNow.AddDays(-50));

        return (new OrgFixture(org.Id, admin.UserId, content.Id), vol);
    }

    private sealed record OrgFixture(int OrgId, string UserId, int ContentId);
}
