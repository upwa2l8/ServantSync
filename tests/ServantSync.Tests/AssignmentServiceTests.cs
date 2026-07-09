using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="AssignmentService.ValidateAsync"/>.
/// Covers the conflict-detection matrix and the training-validity matrix
/// using a fresh in-memory SQLite database per test (via
/// <see cref="SqliteTestBase"/>). The service's real dependencies
/// (DbContextFactory, TrainingService) are wired up; no mocks.
/// </summary>
public class AssignmentServiceTests : SqliteTestBase
{
    // Round-FR-8 fix: was previously `static readonly DateTime Now = new(2026, 7, 9, 14, 0, 0, UTC)`,
    // which pinned test fixtures to a date that has since drifted into the past. The 8
    // ListOpenSlotOccurrences_* tests seed SlotOccurrence rows at `Now.AddHours(1..5)`;
    // AssignmentService.ListOpenSlotOccurrencesAsync filters past occurrences with
    // `if (r.EndUtc <= nowUtc) continue;`, so once wall-clock UTC moved past the pinned
    // `Now + N hours` window, those seeded occurrences were silently dropped and the
    // tests returned 0 rows.
    //
    // Why a `static readonly` cached at class-load and not a property: SQL equality
    // checks (e.g. `db.Assignments.Where(... && a.StartUtc == startUtc ...)` in
    // `AssignmentService.ValidateAsync`) compare fixture timestamps to caller-supplied
    // timestamps. With a property returning `DateTime.UtcNow` on each access, the two
    // `Now` reads inside one test (e.g. seeding the Assignment then calling
    // `ValidateAsync(... startUtc: Now ...)`) can differ by a few microseconds, fail
    // SQL equality, and silently break capacity/conflict tests.
    //
    // Using `DateTime.UtcNow` here (instead of a hard-coded literal) ensures the
    // `_nowPinned` anchor is "right when the test process started", so every future
    // `Now.AddHours(1..5)` fixture read remains in the future relative to wall-clock
    // — no drift even years from now.
    private static readonly DateTime _nowPinned = DateTime.UtcNow;
    private static DateTime Now => _nowPinned;

    private AssignmentService NewService() => new(Factory, new TrainingService(Factory));

    private record Fixture(
        Organization Org,
        Ministry Ministry,
        ServiceSlot Slot,
        ServiceSlot OtherSlot,
        Person Person,
        TrainingContent Content,
        TrainingRequirement OrgRequirement);

    private async Task<Fixture> BuildFixtureAsync()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id, "Sound Tech");
        var otherSlot = TestData.Slot(Factory, ministry.Id, "Vocals");
        var person = TestData.Person(Factory, "Vee", "Volunteer");
        // Per-org scoping (round N): TrainingContent must belong to one
        // Organization. org-scoped requirements imply the content lives in
        // the same org, so pass org.Id here.
        var content = TestData.TrainingContent(Factory, org.Id);
        var req = TestData.Requirement(Factory, content.Id, orgId: org.Id);
        return new Fixture(org, ministry, slot, otherSlot, person, content, req);
    }

    // ---- happy path ----

    [Fact]
    public async Task Validate_NoExistingAssignments_NoTrainingNeeded_ReturnsOk()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Remove the org-wide requirement so we can isolate the conflict check.
        using (var db = await Factory.CreateDbContextAsync())
        {
            db.TrainingRequirements.Remove(f.OrgRequirement);
            await db.SaveChangesAsync();
        }

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now, Now.AddHours(1));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Conflicts);
        Assert.Empty(result.MissingTrainings);
        Assert.NotNull(result.Assignment);
    }

    [Fact]
    public async Task Validate_NonOverlappingExisting_ReturnsOk()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing assignment 10–11 (UTC). New candidate 12–13 → no overlap.
        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(11));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(12), Now.Date.AddHours(13));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Validate_TouchingBoundary_IsNotAConflict()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing 10:00–11:00, candidate 11:00–12:00. Touching (existing.End == candidate.Start)
        // is NOT an overlap per the strict inequality: existing.Start < candidate.End AND existing.End > candidate.Start.
        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(11));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(11), Now.Date.AddHours(12));

        Assert.True(result.Succeeded, string.Join("; ", result.Conflicts));
    }

    // ---- conflict matrix ----

    [Fact]
    public async Task Validate_PartialOverlap_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing 10–12, candidate 11–13. Overlap: 11–12.
        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(12));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(11), Now.Date.AddHours(13));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Conflicts);
    }

    [Fact]
    public async Task Validate_ExistingContainedInCandidate_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing 11–12 fully inside candidate 10–13.
        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(11), Now.Date.AddHours(12));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(10), Now.Date.AddHours(13));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Conflicts);
    }

    [Fact]
    public async Task Validate_CandidateContainedInExisting_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Candidate 11–12 fully inside existing 10–13.
        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(13));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(11), Now.Date.AddHours(12));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Validate_ExcludedAssignmentId_NotCounted()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Re-schedule the same assignment (its own time window conflicts with itself,
        // but excludeAssignmentId should make the validator ignore it).
        var existing = TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(12));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(12),
            excludeAssignmentId: existing.Id);

        Assert.True(result.Succeeded, string.Join("; ", result.Conflicts));
    }

    [Fact]
    public async Task Validate_CancelledExisting_IsNotAConflict()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(12),
            status: AssignmentStatus.Cancelled);
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(11), Now.AddHours(13));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Validate_NoShowExisting_IsNotAConflict()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        TestData.Assignment(Factory, f.Person.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(12),
            status: AssignmentStatus.NoShow);
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(11), Now.AddHours(13));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Validate_OtherPerson_AssignmentIsNotAConflict()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        var otherPerson = TestData.Person(Factory, "Other", "Person");

        // Conflict on the other person shouldn't affect this person's validation.
        TestData.Assignment(Factory, otherPerson.UserId, f.OtherSlot.Id,
            Now.Date.AddHours(10), Now.Date.AddHours(12));
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.Date.AddHours(11), Now.AddHours(13));

        Assert.True(result.Succeeded);
    }

    // ---- training matrix ----

    [Fact]
    public async Task Validate_NoCompletion_FailsWithMissingTraining()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Empty(result.Conflicts);
        Assert.NotEmpty(result.MissingTrainings);
    }

    [Fact]
    public async Task Validate_ExpiredCompletion_FailsWithMissingTraining()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Completed two years ago → expired.
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id,
            Now.AddYears(-2));

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.MissingTrainings);
    }

    [Fact]
    public async Task Validate_RecentCompletion_ReturnsOk()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now.AddDays(-30));

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now, Now.AddHours(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Conflicts) + " | " + string.Join("; ", result.MissingTrainings));
    }

    [Fact]
    public async Task Validate_SlotSpecificRequirementAlsoEnforced()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Add a slot-specific requirement with a DIFFERENT content id
        // (the org requirement already references f.Content.Id, so a
        // completion of f.Content would satisfy both — the slot-specific
        // requirement must reference its own course to be independent).
        // Slot-scoped requirement: the content must belong to the SAME
        // org as the slot's parent ministry, otherwise the per-org gate
        // would make the requirement / content pair incoherent. The slot's
        // parent org is f.Org, so the slot-scoped content lives there too.
        var slotContent = TestData.TrainingContent(Factory, f.Org.Id, "Slot-Specific Training");
        TestData.Requirement(Factory, slotContent.Id, slotId: f.Slot.Id);
        // Org requirement satisfied, but the new slot-specific one isn't.
        TestData.Completion(Factory, f.Person.UserId, f.Content.Id, Now);

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.MissingTrainings);
    }

    // ---- input validation ----

    [Fact]
    public async Task Validate_EndBeforeStart_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ValidateAsync(
            f.Person.UserId, f.Slot.Id, Now.AddHours(1), Now);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Conflicts, c => c.Contains("End time must be after start time"));
    }

    [Fact]
    public async Task Validate_SlotNotFound_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ValidateAsync(
            f.Person.UserId, serviceSlotId: 9999, Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Conflicts, c => c.Contains("not found"));
    }

    // ---- capacity (per ServiceSlot.Capacity, optional SlotOccurrence.CapacityOverride) ----

    /// <summary>
    /// Builds a slot with explicit Capacity AND a single org-wide training
    /// requirement. Returns the slot, a "primary" volunteer for that slot
    /// with valid training completion, and the TrainingContent the
    /// additional volunteers also need to complete before signing up.
    /// </summary>
    private async Task<(ServiceSlot slot, Person primary, TrainingContent content)> BuildSlotWithCapacityAsync(
        int capacity)
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        // Per-org scoping (round N): capacity tests use an org-scoped
        // requirement, so the TrainingContent must belong to that org.
        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id);

        ServiceSlot slot;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            slot = new ServiceSlot
            {
                MinistryId = ministry.Id,
                Name = $"Cap-{capacity}",
                Capacity = capacity,
                IsActive = true,
            };
            db.ServiceSlots.Add(slot);
            await db.SaveChangesAsync();
        }

        var primary = TestData.Person(Factory);
        TestData.Completion(Factory, primary.UserId, content.Id, DateTime.UtcNow);
        return (slot, primary, content);
    }

    /// <summary>Helper: a trained second person for capacity tests.</summary>
    private Person NewTrainedPerson(TrainingContent content, string first = "Other", string last = "Volunteer")
    {
        var p = TestData.Person(Factory, first, last);
        TestData.Completion(Factory, p.UserId, content.Id, DateTime.UtcNow);
        return p;
    }

    [Fact]
    public async Task Validate_CapacityOne_TwoVolunteersAtSameTime_SecondFails()
    {
        // Capacity=1; first volunteer takes the only spot via TestData.Assignment
        // (which PERSISTS). Then a second volunteer's ValidateAsync should
        // see count=1, fail the capacity gate.
        var (slot, primary, content) = await BuildSlotWithCapacityAsync(1);
        var other = NewTrainedPerson(content);

        TestData.Assignment(Factory, primary.UserId, slot.Id, Now, Now.AddHours(1));

        var svc = NewService();
        var result = await svc.ValidateAsync(other.UserId, slot.Id, Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Conflicts, c => c.Contains("full"));
    }

    [Fact]
    public async Task Validate_CapacityThree_AllThreeSucceed_FourthFails()
    {
        var (slot, p1, content) = await BuildSlotWithCapacityAsync(3);
        var p2 = NewTrainedPerson(content, "Second", "V");
        var p3 = NewTrainedPerson(content, "Third", "V");
        var p4 = NewTrainedPerson(content, "Fourth", "V");

        // Place the three that fit.
        TestData.Assignment(Factory, p1.UserId, slot.Id, Now, Now.AddHours(1));
        TestData.Assignment(Factory, p2.UserId, slot.Id, Now, Now.AddHours(1));
        TestData.Assignment(Factory, p3.UserId, slot.Id, Now, Now.AddHours(1));

        var svc = NewService();
        var fourth = await svc.ValidateAsync(p4.UserId, slot.Id, Now, Now.AddHours(1));

        Assert.False(fourth.Succeeded);
        Assert.Contains(fourth.Conflicts, c => c.Contains("3 of 3"));
    }

    [Fact]
    public async Task Validate_CapacityRefills_WhenExistingCancelled()
    {
        // Cancellation re-opens the slot for a new volunteer.
        var (slot, p1, content) = await BuildSlotWithCapacityAsync(2);
        var p2 = NewTrainedPerson(content, "Second", "V");
        var p3 = NewTrainedPerson(content, "Third", "V");

        TestData.Assignment(Factory, p1.UserId, slot.Id, Now, Now.AddHours(1));
        TestData.Assignment(Factory, p2.UserId, slot.Id, Now, Now.AddHours(1),
            status: AssignmentStatus.Scheduled);

        // Cancel p2's assignment; capacity refills.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var a = await db.Assignments.FirstAsync(x => x.PersonUserId == p2.UserId && x.ServiceSlotId == slot.Id);
            a.Status = AssignmentStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var svc = NewService();
        var thirdAttempt = await svc.ValidateAsync(p3.UserId, slot.Id, Now, Now.AddHours(1));

        Assert.True(thirdAttempt.Succeeded, string.Join("; ", thirdAttempt.Conflicts)
            + " | " + string.Join("; ", thirdAttempt.MissingTrainings));
    }

    [Fact]
    public async Task Validate_CapacityOverride_WinsOverSlotDefault()
    {
        // Slot.Capacity = 1, but SlotOccurrence.CapacityOverride = 3. Validate
        // sees count + capacity correctly even with override.
        var (slot, p1, content) = await BuildSlotWithCapacityAsync(1);
        var p2 = NewTrainedPerson(content, "Second", "V");
        var p3 = NewTrainedPerson(content, "Third", "V");
        var p4 = NewTrainedPerson(content, "Fourth", "V");

        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.SlotOccurrences.Add(new SlotOccurrence
            {
                ServiceSlotId = slot.Id,
                StartUtc = Now,
                EndUtc = Now.AddHours(1),
                CapacityOverride = 3,
            });
            await db.SaveChangesAsync();
        }

        // Three sign-ups all fit the override.
        TestData.Assignment(Factory, p1.UserId, slot.Id, Now, Now.AddHours(1));
        TestData.Assignment(Factory, p2.UserId, slot.Id, Now, Now.AddHours(1));
        TestData.Assignment(Factory, p3.UserId, slot.Id, Now, Now.AddHours(1));

        var svc = NewService();
        var fourth = await svc.ValidateAsync(p4.UserId, slot.Id, Now, Now.AddHours(1));
        Assert.False(fourth.Succeeded);
    }

    [Fact]
    public async Task CreateSlotOccurrence_DuplicateAtSameTime_Fails()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        var svc = NewService();

        var first = await svc.CreateSlotOccurrenceAsync(slot.Id, Now, Now.AddHours(1), notes: null, capacityOverride: null);
        Assert.True(first.Succeeded);

        var dup = await svc.CreateSlotOccurrenceAsync(slot.Id, Now, Now.AddHours(1), notes: null, capacityOverride: null);
        Assert.False(dup.Succeeded);
        Assert.NotNull(dup.Error);
        Assert.Contains("already scheduled", dup.Error!);
    }

    [Fact]
    public async Task CreateSlotOccurrence_OnInactiveSlot_Fails()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        var svc = NewService();
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.ServiceSlots.First(s => s.Id == slot.Id).IsActive = false;
            await db.SaveChangesAsync();
        }

        var r = await svc.CreateSlotOccurrenceAsync(slot.Id, Now, Now.AddHours(1), notes: null, capacityOverride: null);
        Assert.False(r.Succeeded);
        Assert.Contains("inactive", r.Error!);
    }

    // ---- ministryIdsFilter (Open-page "My ministries" / "All my orgs" toggle) ----

    /// <summary>
    /// Builds a fixture with two ministries in the same org, each with an
    /// open occurrence. Returns enough handles to drive the filter tests:
    /// the user, the two ministries, the org, and the open occurrences'
    /// training content.
    /// </summary>
    private async Task<(Organization org, Ministry ministryA, Ministry ministryB, Person user, TrainingContent content)>
        BuildTwoMinistryFixtureAsync()
    {
        var org = TestData.Org(Factory);
        var ministryA = TestData.Ministry(Factory, org.Id, "Worship");
        var ministryB = TestData.Ministry(Factory, org.Id, "Kids");
        // Per-org scoping (round N): TrainingContent must belong to one
        // Organization, and the org-scoped requirement here implies
        // content lives in the same org as the ministry.
        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id);

        var slotA = new ServiceSlot { MinistryId = ministryA.Id, Name = "A-Slot", IsActive = true };
        var slotB = new ServiceSlot { MinistryId = ministryB.Id, Name = "B-Slot", IsActive = true };
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.ServiceSlots.AddRange(slotA, slotB);
            await db.SaveChangesAsync();
        }

        await using (var db2 = await Factory.CreateDbContextAsync())
        {
            db2.SlotOccurrences.AddRange(
                new SlotOccurrence { ServiceSlotId = slotA.Id, StartUtc = Now.AddHours(1), EndUtc = Now.AddHours(2) },
                new SlotOccurrence { ServiceSlotId = slotB.Id, StartUtc = Now.AddHours(3), EndUtc = Now.AddHours(4) });
            await db2.SaveChangesAsync();
        }

        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Completion(Factory, user.UserId, content.Id, DateTime.UtcNow);
        return (org, ministryA, ministryB, user, content);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithoutFilter_ReturnsBothMinistries()
    {
        // Default behavior (no filter supplied) returns every open shift
        // across the user's orgs. This is the "All my orgs" toggle on
        // /Open.
        var (_, _, _, user, _) = await BuildTwoMinistryFixtureAsync();
        var svc = NewService();

        var rows = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.MinistryName == "Worship");
        Assert.Contains(rows, r => r.MinistryName == "Kids");
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithSingleMinistryFilter_ReturnsOnlyThatMinistry()
    {
        // Filter narrows to exactly one ministry IDs list: only that
        // ministry's open shifts appear. This is the "My ministries"
        // toggle with exactly one followed ministry.
        var (_, ministryA, _, user, _) = await BuildTwoMinistryFixtureAsync();
        var svc = NewService();
        var filter = new[] { ministryA.Id };

        var rows = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7), filter);

        Assert.Single(rows);
        Assert.Equal("Worship", rows[0].MinistryName);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithMultipleMinistryFilter_ReturnsUnion()
    {
        // Two-of-two filter: returns both (the union semantics). Validates
        // the "filter is a whitelist, not a single-id pivot" contract.
        var (_, ministryA, ministryB, user, _) = await BuildTwoMinistryFixtureAsync();
        var svc = NewService();
        var filter = new[] { ministryA.Id, ministryB.Id };

        var rows = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7), filter);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.MinistryName == "Worship");
        Assert.Contains(rows, r => r.MinistryName == "Kids");
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithMismatchedFilter_ReturnsEmpty()
    {
        // Filter pointing at a ministry the user has no relation to.
        // Real-world equivalent: a stale filter id — should return
        // nothing rather than crash or fall back silently.
        var (_, _, _, user, _) = await BuildTwoMinistryFixtureAsync();
        var svc = NewService();
        var filter = new[] { 99999 };

        var rows = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7), filter);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_FilterEmptyCollection_DoesNotThrow()
    {
        // Defensive: an empty filter collection (not null but containing
        // zero items) should be treated as "no filter" rather than
        // producing an IN () clause that the SQLite provider rejects.
        var (_, _, _, user, _) = await BuildTwoMinistryFixtureAsync();
        var svc = NewService();
        var filter = Array.Empty<int>();

        var rows = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7), filter);

        // Empty collection treated as "all my orgs" → both shifts appear.
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithSlotFilter_NarrowsToMatchingSlotsOnlyRoundFR7()
    {
        // Round-FR-7: the 3-way /Open "My slots" filter tests with slot
        // IDs instead of ministry IDs. The two-ministry fixture gives us
        // slotA (in ministry A) and slotB (in ministry B); the slot filter
        // should narrow the row count to exactly the matching slot's
        // occurrence regardless of ministry membership.
        var (_, _, _, user, _) = await BuildTwoMinistryFixtureAsync();
        // Look up the slot IDs the helper created (slotA is in ministry A,
        // slotB is in ministry B — the helper names them "A-Slot" and
        // "B-Slot" via the inline ServiceSlot constructor it calls).
        int slotAId, slotBId;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            slotAId = await db.ServiceSlots.Where(s => s.Name == "A-Slot").Select(s => s.Id).FirstAsync();
            slotBId = await db.ServiceSlots.Where(s => s.Name == "B-Slot").Select(s => s.Id).FirstAsync();
        }

        var svc = NewService();

        // No filter: both ministries' slots' occurrences appear (2 rows).
        var unfiltered = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7));
        Assert.Equal(2, unfiltered.Count);

        // Slot filter narrows to slotA only — 1 row, slotA's slot.
        var narrowed = await svc.ListOpenSlotOccurrencesAsync(
            user.UserId, Now, Now.AddDays(7), slotIdsFilter: new[] { slotAId });
        Assert.Single(narrowed);
        Assert.Equal(slotAId, narrowed[0].ServiceSlotId);
        Assert.Equal("A-Slot", narrowed[0].SlotName);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithSlotFilter_NoMatches_ReturnsEmptyRoundFR7()
    {
        // Edge case: slot filter points at a slot that doesn't exist OR
        // doesn't have an open occurrence. Service returns empty rather
        // than crashing.
        var (_, _, _, user, _) = await BuildTwoMinistryFixtureAsync();
        var svc = NewService();

        var empty1 = await svc.ListOpenSlotOccurrencesAsync(
            user.UserId, Now, Now.AddDays(7), slotIdsFilter: new[] { 99999 });
        Assert.Empty(empty1);

        var empty2 = await svc.ListOpenSlotOccurrencesAsync(
            user.UserId, Now, Now.AddDays(7), slotIdsFilter: Array.Empty<int>());
        // Empty-collection sentinel = "no filter" per the existing
        // ministryFilter convention, so the result is the unfiltered
        // set (both slots' occurrences).
        Assert.Equal(2, empty2.Count);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_WithBothFilters_AppliesIntersectionRoundFR7()
    {
        // Dual filter: ministryA narrowed + slotInB in the slot filter.
        // The intersection is empty (slotInB lives in ministryB, not
        // ministryA) — service must return 0 rows. This pins the AND
        // composition semantics: both filters non-empty → AND, not OR.
        var (_, ministryA, _, user, _) = await BuildTwoMinistryFixtureAsync();
        int slotBId;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            slotBId = await db.ServiceSlots.Where(s => s.Name == "B-Slot").Select(s => s.Id).FirstAsync();
        }

        var svc = NewService();
        var rows = await svc.ListOpenSlotOccurrencesAsync(user.UserId, Now, Now.AddDays(7),
            ministryIdsFilter: new[] { ministryA.Id },
            slotIdsFilter: new[] { slotBId });

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ListOpenSlotOccurrences_FilterCrossOrg_ReturnsOnlyFromUserOrgs()
    {
        // Critical sandbox: the filter narrows by ministry-id AT THE
        // CANDIDATE SET ONLY. The outer "only from my orgs" constraint
        // must STILL hold even if the filter contains ministry ids that
        // belong to OTHER organizations. A regression that ANDs the
        // filter at the top level would expose cross-org shifts.
        var (orgA, ministryA, _, userA, content) = await BuildTwoMinistryFixtureAsync();
        var orgB = TestData.Org(Factory, "Other Org");
        var ministryOfOtherOrg = TestData.Ministry(Factory, orgB.Id, "Other-Min");

        // Put an open occurrence in Org B's ministry so we have something
        // for the filter to falsely-match.
        var otherSlot = new ServiceSlot { MinistryId = ministryOfOtherOrg.Id, Name = "Other-Slot", IsActive = true };
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.ServiceSlots.Add(otherSlot);
            await db.SaveChangesAsync();
        }
        await using (var db2 = await Factory.CreateDbContextAsync())
        {
            db2.SlotOccurrences.Add(new SlotOccurrence
            {
                ServiceSlotId = otherSlot.Id,
                StartUtc = Now.AddHours(5),
                EndUtc = Now.AddHours(6),
            });
            await db2.SaveChangesAsync();
        }

        var svc = NewService();
        // Filter that BOTH legitimately references one of userA's
        // ministries AND falsely references Org B's ministry. The
        // sandbox check must filter out the cross-org row.
        var filter = new[] { ministryA.Id, ministryOfOtherOrg.Id };

        var rows = await svc.ListOpenSlotOccurrencesAsync(userA.UserId, Now, Now.AddDays(7), filter);

        Assert.Single(rows);
        Assert.Equal("Worship", rows[0].MinistryName);
    }

    // ---- original happy-path coverage ----

    [Fact]
    public async Task ListOpenSlotOccurrences_ReturnsOnlyOpenAndUntaken()
    {
        // Two slots in the user's org. The first has an open occurrence with
        // count<capacity; the second is at capacity. Only the first appears.
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        // Per-org scoping (round N): training content must belong to
        // one Organization; the org-scoped requirement pinpoints that
        // organization, so the test content lives there too.
        var content = TestData.TrainingContent(Factory, org.Id);
        TestData.Requirement(Factory, content.Id, orgId: org.Id);

        var slotOpen = new ServiceSlot { MinistryId = ministry.Id, Name = "Open Slot", IsActive = true, Capacity = 3 };
        var slotFull = new ServiceSlot { MinistryId = ministry.Id, Name = "Full Slot", IsActive = true, Capacity = 1 };
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.ServiceSlots.AddRange(slotOpen, slotFull);
            await db.SaveChangesAsync();
        }

        var me = TestData.Person(Factory, "Me", "Volunteer");
        TestData.Membership(Factory, me.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Completion(Factory, me.UserId, content.Id, DateTime.UtcNow);

        // Open occurrence on slotOpen: 1 of 3 signed up.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.SlotOccurrences.Add(new SlotOccurrence
            {
                ServiceSlotId = slotOpen.Id, StartUtc = Now.AddHours(1), EndUtc = Now.AddHours(2),
            });
            await db.SaveChangesAsync();
        }
        var first = TestData.Person(Factory, "First", "V");
        TestData.Membership(Factory, first.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Completion(Factory, first.UserId, content.Id, DateTime.UtcNow);
        TestData.Assignment(Factory, first.UserId, slotOpen.Id, Now.AddHours(1), Now.AddHours(2));

        // Full occurrence on slotFull: 1 of 1 signed up.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.SlotOccurrences.Add(new SlotOccurrence
            {
                ServiceSlotId = slotFull.Id, StartUtc = Now.AddHours(2), EndUtc = Now.AddHours(3),
            });
            await db.SaveChangesAsync();
        }
        TestData.Assignment(Factory, first.UserId, slotFull.Id, Now.AddHours(2), Now.AddHours(3));

        var svc = NewService();
        var rows = await svc.ListOpenSlotOccurrencesAsync(me.UserId, Now, Now.AddDays(7));

        Assert.Single(rows);
        Assert.Equal("Open Slot", rows[0].SlotName);
        Assert.Equal(3, rows[0].Capacity);
        Assert.Equal(1, rows[0].SignedUpCount);
        Assert.False(rows[0].AlreadySignedUp);
        Assert.True(rows[0].TrainingCompliant);
    }

    // ---- ScheduleOpenShiftSeriesAsync (weekly recurrence of SlotOccurrences) ----

    /// <summary>
    /// Picks the next Sunday >= <paramref name="fromLocal"/> and returns it
    /// as a (date, tzId, stime) triple that ScheduleOpenShiftSeriesAsync can
    /// walk. The local zone is whatever the test runner runs in (UTC on CI).
    /// </summary>
    private static (DateTime firstDate, DateTime endDate, string tzId, TimeSpan stime) WeeklySundayBlock(
        DateTime fromLocal, int spanDays)
    {
        var date = fromLocal.Date;
        var daysUntil = ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7;
        var firstDate = date.AddDays(daysUntil);
        return (firstDate, firstDate.AddDays(spanDays), TimeZoneInfo.Local.Id, TimeSpan.FromHours(14));
    }

    [Fact]
    public async Task ScheduleOpenShiftSeries_HappyPath_CreatesAllOccurrences()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        var svc = NewService();

        var (firstDate, endDate, tzId, stime) = WeeklySundayBlock(new DateTime(2026, 7, 12), 28);

        var result = await svc.ScheduleOpenShiftSeriesAsync(
            slot.Id, DayOfWeek.Sunday, stime, 60,
            firstDate, endDate, tzId, capacityOverride: null, notes: "Test series");

        Assert.Equal(4, result.Created.Count); // 4 Sundays in 28 days
        Assert.Empty(result.Skipped);
        Assert.False(result.CapReached);
        foreach (var occ in result.Created)
        {
            Assert.Equal(slot.Id, occ.ServiceSlotId);
            Assert.True(occ.EndUtc > occ.StartUtc);
            Assert.True(occ.StartUtc >= DateTime.UtcNow.AddSeconds(-5)); // future-leaning
        }
    }

    [Fact]
    public async Task ScheduleOpenShiftSeries_DuplicateAtOneDate_IsSkipped()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        var svc = NewService();
        var (firstDate, endDate, tzId, stime) = WeeklySundayBlock(new DateTime(2026, 7, 12), 28);

        // Pre-queue an open shift at the FIRST Sunday of the series window so
        // the recurrence's first iteration lands on a duplicate.
        var firstLocal = DateTime.SpecifyKind(firstDate + stime, DateTimeKind.Unspecified);
        var firstUtc = TimeZoneInfo.ConvertTimeToUtc(firstLocal, TimeZoneInfo.FindSystemTimeZoneById(tzId));
        var preExisting = await svc.CreateSlotOccurrenceAsync(
            slot.Id, firstUtc, firstUtc.AddMinutes(60), "Pre-existing", capacityOverride: null);
        Assert.True(preExisting.Succeeded);

        var result = await svc.ScheduleOpenShiftSeriesAsync(
            slot.Id, DayOfWeek.Sunday, stime, 60,
            firstDate, endDate, tzId, capacityOverride: null, notes: null);

        // 4 iterations: 1 duplicate + 3 fresh creates.
        Assert.Equal(1, result.Skipped.Count);
        Assert.Contains(result.Skipped[0].Reasons, r => r.Contains("already exists"));
        Assert.Equal(3, result.Created.Count);
    }

    [Fact]
    public async Task ScheduleOpenShiftSeries_OnInactiveSlot_Throws()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.ServiceSlots.First(s => s.Id == slot.Id).IsActive = false;
            await db.SaveChangesAsync();
        }
        var svc = NewService();
        var (firstDate, endDate, tzId, stime) = WeeklySundayBlock(new DateTime(2026, 7, 12), 28);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ScheduleOpenShiftSeriesAsync(
            slot.Id, DayOfWeek.Sunday, stime, 60,
            firstDate, endDate, tzId, capacityOverride: null, notes: null));
    }

    [Fact]
    public async Task ScheduleOpenShiftSeries_EndBeforeStart_Throws()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        var svc = NewService();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ScheduleOpenShiftSeriesAsync(
            slot.Id, DayOfWeek.Sunday, TimeSpan.FromHours(14), 60,
            DateTime.Today, DateTime.Today, TimeZoneInfo.Local.Id,
            capacityOverride: null, notes: null));
    }

    [Fact]
    public async Task ScheduleOpenShiftSeries_NegativeCapacityOverride_Throws()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(2);
        var svc = NewService();
        var (firstDate, endDate, tzId, stime) = WeeklySundayBlock(new DateTime(2026, 7, 12), 28);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ScheduleOpenShiftSeriesAsync(
            slot.Id, DayOfWeek.Sunday, stime, 60,
            firstDate, endDate, tzId, capacityOverride: -1, notes: null));
    }

    [Fact]
    public async Task ScheduleOpenShiftSeries_CapacityOverride_AppliedToEachOccurrence()
    {
        var (slot, _, _) = await BuildSlotWithCapacityAsync(1);
        var svc = NewService();
        var (firstDate, endDate, tzId, stime) = WeeklySundayBlock(new DateTime(2026, 7, 12), 21);

        var result = await svc.ScheduleOpenShiftSeriesAsync(
            slot.Id, DayOfWeek.Sunday, stime, 60,
            firstDate, endDate, tzId, capacityOverride: 4, notes: "Bigger crew needed");

        Assert.Equal(3, result.Created.Count);
        foreach (var occ in result.Created)
        {
            // Each created occurrence carries the explicit override even
            // though the slot default is 1 — downstream capacity checks
            // (in ValidateAsync) honor the override.
            Assert.Equal(4, occ.CapacityOverride);
        }
    }
}
