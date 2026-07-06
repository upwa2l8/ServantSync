using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Integration tests for <see cref="TrainingSessionService"/>.
/// Covers the round-FR-2 spec (PLAN.md): in-person scheduled training
/// sessions with manual-completion audit. Exercises every
/// decision-enforced service-layer invariant — capacity enforcement
/// (Q1), notes required on bulk mark (Q5), engagement-gate bypass on
/// manual mark (Q6), latest-wins completion upsert (Q7), and the
/// perm/membership gates that protect against cross-org / non-member
/// write attempts.
/// </summary>
public class TrainingSessionServiceTests : SqliteTestBase
{
    private TrainingSessionService NewService() => new TrainingSessionService(Factory);

    // ─── CreateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CoordinatorOfOrg_Succeeds_AndPersistsShape()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);

        var start = DateTime.UtcNow.AddDays(7);
        var end = start.AddHours(1);
        var result = await NewService().CreateAsync(
            organizationId: org.Id,
            title: "Safe Spaces 101 — in person",
            description: "Walk-through with handout.",
            location: "Room 12",
            startUtc: start,
            endUtc: end,
            maxAttendees: 20,
            trainingContentId: null,
            callerUserId: coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync();
        Assert.Equal(org.Id, session.OrganizationId);
        Assert.Equal("Safe Spaces 101 — in person", session.Title);
        Assert.Equal("Room 12", session.Location);
        Assert.Equal(20, session.MaxAttendees);
        Assert.Equal(TrainingSessionStatus.Scheduled, session.Status);
        Assert.Equal(coord.UserId, session.CreatedByUserId);
    }

    [Fact]
    public async Task CreateAsync_AdminOfOrg_Succeeds()
    {
        // Pin that Admins (not just Coordinators) can create sessions.
        var org = TestData.Org(Factory, "Org A");
        var admin = TestData.Person(Factory, "Alex", "Admin");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var start = DateTime.UtcNow.AddDays(7);
        var result = await NewService().CreateAsync(
            org.Id, "Admin session", null, "Office", start, start.AddHours(1),
            null, null, admin.UserId);

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);
    }

    [Fact]
    public async Task CreateAsync_PlainVolunteerOfOrg_PermissionDenied()
    {
        // Only Admin/Coordinator can create; Volunteers can't.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "Crossing the line", null, "Hallway", start, start.AddHours(1),
            null, null, volunteer.UserId);

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task CreateAsync_CoordinatorOfOtherOrg_PermissionDenied()
    {
        // Cross-org authority doesn't transfer; an org-2 Coordinator
        // can't stage a session in org-1.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var coordB = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coordB.UserId, orgB.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            orgA.Id, "Foreign-org session", null, "Anywhere", start, start.AddHours(1),
            null, null, coordB.UserId);

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task CreateAsync_EmptyTitle_ReturnsValidationFailed()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "", null, "Room", start, start.AddHours(1),
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task CreateAsync_EmptyLocation_ReturnsValidationFailed()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "Valid title", null, "", start, start.AddHours(1),
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task CreateAsync_EndUtcNotAfterStartUtc_ReturnsValidationFailed()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "Reversed times", null, "Room", start, start,
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task CreateAsync_NegativeMaxAttendees_ReturnsValidationFailed()
    {
        // Decision Q3: MaxAttendees<=0 invalid. Same reason as EndUtc
        // — the model trusts the input, the service refuses.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "Bad capacity", null, "Room", start, start.AddHours(1),
            maxAttendees: -3, trainingContentId: null, callerUserId: coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task CreateAsync_ContentFromSameOrg_Succeeds()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Welcome Training");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "Linked session", null, "Room", start, start.AddHours(1),
            null, content.Id, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync();
        Assert.Equal(content.Id, session.TrainingContentId);
    }

    [Fact]
    public async Task CreateAsync_ContentFromForeignOrg_ValidationFailed()
    {
        // Defense in depth — TrainingContent is already per-org-scoped,
        // but a future content-ownership refactor must not silently
        // weaken this invariant; the explicit cross-org check catches it.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var foreignContent = TestData.TrainingContent(Factory, orgB.Id, "Foreign B training");
        var coordA = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coordA.UserId, orgA.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            orgA.Id, "Stolen-content session", null, "Room", start, start.AddHours(1),
            null, foreignContent.Id, coordA.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task CreateAsync_EmptyCallerUserId_PermissionDenied()
    {
        // Defense in depth: empty userId string short-circuits the
        // permission gate before the membership-row query. Pins
        // the "caller isn't authenticated" defense so it can't be
        // accidentally eroded by a future nullable-handling refactor.
        var org = TestData.Org(Factory, "Org A");
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().CreateAsync(
            org.Id, "Anonymous", null, "Room", start, start.AddHours(1),
            null, null, "");

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    // ─── EditAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAsync_HappyPath_PersistsUpdates()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id, "Original");
        var newStart = DateTime.UtcNow.AddDays(14);
        var newEnd = newStart.AddHours(2);

        var result = await NewService().EditAsync(
            s.Id, "Renamed", "Refreshed description", "New room",
            newStart, newEnd, 10, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal("Renamed", session.Title);
        Assert.Equal("Refreshed description", session.Description);
        Assert.Equal("New room", session.Location);
        Assert.Equal(newStart, session.StartUtc);
        Assert.Equal(10, session.MaxAttendees);
    }

    [Fact]
    public async Task EditAsync_UnknownSessionId_NotFound()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var start = DateTime.UtcNow.AddDays(7);

        var result = await NewService().EditAsync(
            999_999, "Anything", null, "Anywhere", start, start.AddHours(1),
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.NotFound, result);
    }

    [Fact]
    public async Task EditAsync_CancelledSession_AlreadyCancelled()
    {
        // Spec: completed/cancelled sessions are audit-trail artifacts.
        // Coord can't edit history.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id, "Cancelled session",
            status: TrainingSessionStatus.Cancelled);

        var result = await NewService().EditAsync(
            s.Id, "Post-cancel edit", null, "Fix-it room", s.StartUtc, s.EndUtc,
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.AlreadyCancelled, result);
    }

    [Fact]
    public async Task EditAsync_CompletedSession_AlreadyCompleted()
    {
        // Same reason as Cancelled; the mark has finalized attendance.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id, "Completed session",
            status: TrainingSessionStatus.Completed);

        var result = await NewService().EditAsync(
            s.Id, "Post-complete edit", null, "Room", s.StartUtc, s.EndUtc,
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.AlreadyCompleted, result);
    }

    [Fact]
    public async Task EditAsync_BadInputs_ValidationFailed()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id);
        var start = DateTime.UtcNow.AddDays(7);

        // End before Start.
        var result = await NewService().EditAsync(
            s.Id, "Valid", null, "Valid", start.AddHours(1), start,
            null, null, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    // ─── CancelAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_HappyPath_FlipsStatusToCancelled()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().CancelAsync(s.Id, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync();
        Assert.Equal(TrainingSessionStatus.Cancelled, session.Status);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_AlreadyCancelled()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id, status: TrainingSessionStatus.Cancelled);

        var result = await NewService().CancelAsync(s.Id, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.AlreadyCancelled, result);
    }

    [Fact]
    public async Task CancelAsync_CompletedSession_AlreadyCompleted()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id, status: TrainingSessionStatus.Completed);

        var result = await NewService().CancelAsync(s.Id, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.AlreadyCompleted, result);
    }

    [Fact]
    public async Task CancelAsync_UnknownSessionId_NotFound()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewService().CancelAsync(999_999, coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.NotFound, result);
    }

    [Fact]
    public async Task CancelAsync_VolunteerCaller_PermissionDenied()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().CancelAsync(s.Id, volunteer.UserId);

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    // ─── ListUpcomingAsync / ListPastAsync / GetAsync ───────────────────────

    [Fact]
    public async Task ListUpcomingAsync_OnlyReturnsUpcomingInWindow_OwnsOrg()
    {
        // Three sessions in Org A (1 upcoming, 1 past, 1 cancelled) plus
        // 1 upcoming in Org B — Org A ask must yield exactly the 1 upcoming.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        TestData.TrainingSession(Factory, orgA.Id, "A upcoming",
            startUtc: DateTime.UtcNow.AddDays(7), endUtc: DateTime.UtcNow.AddDays(7).AddHours(1));
        TestData.TrainingSession(Factory, orgA.Id, "A past",
            startUtc: DateTime.UtcNow.AddDays(-7), endUtc: DateTime.UtcNow.AddDays(-7).AddHours(1));
        TestData.TrainingSession(Factory, orgA.Id, "A cancelled",
            startUtc: DateTime.UtcNow.AddDays(7), endUtc: DateTime.UtcNow.AddDays(7).AddHours(1),
            status: TrainingSessionStatus.Cancelled);
        TestData.TrainingSession(Factory, orgB.Id, "B upcoming");

        var result = await NewService().ListUpcomingAsync(orgA.Id);

        var session = Assert.Single(result);
        Assert.Equal("A upcoming", session.Title);
    }

    [Fact]
    public async Task ListUpcomingAsync_Beyond60DayWindow_IsFiltered()
    {
        // The 60-day forward window means sessions further out aren't
        // yet "upcoming" for the page; they're listed via a different
        // surface (Calendar) once the date approaches.
        var org = TestData.Org(Factory, "Org A");
        TestData.TrainingSession(Factory, org.Id, "Just outside window",
            startUtc: DateTime.UtcNow.AddDays(120), endUtc: DateTime.UtcNow.AddDays(120).AddHours(1));

        var result = await NewService().ListUpcomingAsync(org.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListPastAsync_ReturnsCompletedCancelledAndFinishedSessions()
    {
        var org = TestData.Org(Factory, "Org A");
        TestData.TrainingSession(Factory, org.Id, "Completed session",
            startUtc: DateTime.UtcNow.AddDays(-14),
            endUtc: DateTime.UtcNow.AddDays(-14).AddHours(1),
            status: TrainingSessionStatus.Completed);
        TestData.TrainingSession(Factory, org.Id, "Cancelled session",
            startUtc: DateTime.UtcNow.AddDays(-10),
            endUtc: DateTime.UtcNow.AddDays(-10).AddHours(1),
            status: TrainingSessionStatus.Cancelled);
        TestData.TrainingSession(Factory, org.Id, "End-time passed",
            startUtc: DateTime.UtcNow.AddDays(-3),
            endUtc: DateTime.UtcNow.AddDays(-3).AddHours(1),
            status: TrainingSessionStatus.Scheduled);
        // Outside sinceUtc filter — should NOT appear.
        TestData.TrainingSession(Factory, org.Id, "Far past",
            startUtc: DateTime.UtcNow.AddDays(-365),
            endUtc: DateTime.UtcNow.AddDays(-365).AddHours(1),
            status: TrainingSessionStatus.Completed);

        var since = DateTime.UtcNow.AddDays(-30);
        var result = await NewService().ListPastAsync(org.Id, since);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, s => s.Title == "Completed session");
        Assert.Contains(result, s => s.Title == "Cancelled session");
        Assert.Contains(result, s => s.Title == "End-time passed");
    }

    [Fact]
    public async Task GetAsync_KnownSession_ReturnsEagerLoadedShape()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Linked training");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id,
            trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, coord.UserId);

        var result = await NewService().GetAsync(s.Id);

        Assert.NotNull(result);
        Assert.NotNull(result!.TrainingContent);
        Assert.Equal(content.Id, result.TrainingContent!.Id);
        Assert.Single(result.Attendees);
    }

    [Fact]
    public async Task GetAsync_UnknownSessionId_ReturnsNull()
    {
        var result = await NewService().GetAsync(999_999);
        Assert.Null(result);
    }

    // ─── SignUpAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task SignUpAsync_OrgMemberOnScheduledSession_Succeeds()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().SignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.SignedUp, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.TrainingSessionAttendees.AnyAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId));
    }

    [Fact]
    public async Task SignUpAsync_NonOrgMember_NotFound()
    {
        // Don't leak session id existence to outsiders (decision
        // Q-permission): outsider gets NotFound, not PermissionDenied.
        var org = TestData.Org(Factory, "Org A");
        var s = TestData.TrainingSession(Factory, org.Id);
        var stranger = TestData.Person(Factory, "Stranger", "McNobody");
        // Intentionally NO membership for stranger.

        var result = await NewService().SignUpAsync(s.Id, stranger.UserId, callerUserId: stranger.UserId);

        Assert.Equal(TrainingSessionSignupResult.NotFound, result);
    }

    [Fact]
    public async Task SignUpAsync_AlreadySignedUp_AlreadySignedUp()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.AlreadySignedUp, result);
    }

    [Fact]
    public async Task SignUpAsync_CancelledSession_SessionCancelled()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, status: TrainingSessionStatus.Cancelled);

        var result = await NewService().SignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.SessionCancelled, result);
    }

    [Fact]
    public async Task SignUpAsync_SessionAtCapacity_SessionFull()
    {
        // Decision Q1: ENFORCE. MaxAttendees=2, two already on the list,
        // third rejected.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var v1 = TestData.Person(Factory, "Vicky1", "V");
        var v2 = TestData.Person(Factory, "Vicky2", "V");
        var v3 = TestData.Person(Factory, "Vicky3", "V");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, v1.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v2.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v3.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, maxAttendees: 2);
        TestData.TrainingSessionAttendee(Factory, s.Id, v1.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, v2.UserId);

        var result = await NewService().SignUpAsync(s.Id, v3.UserId, callerUserId: v3.UserId);

        Assert.Equal(TrainingSessionSignupResult.SessionFull, result);
    }

    [Fact]
    public async Task SignUpAsync_SessionFull_NoRowInserted()
    {
        // Companion test: capacity enforcement doesn't quietly accept
        // and then fail-render — verify no attendee row is created.
        var org = TestData.Org(Factory, "Org A");
        var v1 = TestData.Person(Factory, "Vicky1", "V");
        var v2 = TestData.Person(Factory, "Vicky2", "V");
        var v3 = TestData.Person(Factory, "Vicky3", "V");
        TestData.Membership(Factory, v1.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v2.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v3.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, maxAttendees: 1);
        TestData.TrainingSessionAttendee(Factory, s.Id, v1.UserId);

        await NewService().SignUpAsync(s.Id, v2.UserId, callerUserId: v2.UserId);
        await NewService().SignUpAsync(s.Id, v3.UserId, callerUserId: v3.UserId);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.TrainingSessionAttendees.CountAsync());
    }

    [Fact]
    public async Task SignUpAsync_NoCapacityLimit_AcceptsArbitraryRoster()
    {
        // Defense in depth on the null-max path — without the null
        // check, future refactors could misinterpret 0 or leave the
        // session permanently locked.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id, maxAttendees: null);

        // 10 sign-ups, no resistance.
        for (var i = 0; i < 10; i++)
        {
            var v = TestData.Person(Factory, $"V{i}", "V");
            TestData.Membership(Factory, v.UserId, org.Id, OrganizationRole.Volunteer);
            var result = await NewService().SignUpAsync(s.Id, v.UserId, callerUserId: v.UserId);
            Assert.Equal(TrainingSessionSignupResult.SignedUp, result);
        }
    }

    [Fact]
    public async Task SignUpAsync_UnknownSessionId_NotFound()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SignUpAsync(999_999, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.NotFound, result);
    }

    // ─── CancelSignUpAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CancelSignUpAsync_SignedUpAttendee_Succeeds()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().CancelSignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.Cancelled, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.TrainingSessionAttendees.AnyAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId));
    }

    [Fact]
    public async Task CancelSignUpAsync_NotSignedUp_NotSignedUp()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().CancelSignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.NotSignedUp, result);
    }

    [Fact]
    public async Task CancelSignUpAsync_AlreadyMarkedAttended_AlreadyMarkedAttended()
    {
        // Per PLAN edge case: the marker owns the audit trail; volunteer
        // can't self-cancel once attendance is recorded.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId, attended: true);

        var result = await NewService().CancelSignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.AlreadyMarkedAttended, result);
    }

    [Fact]
    public async Task CancelSignUpAsync_SessionCancelled_SessionCancelled()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, status: TrainingSessionStatus.Cancelled);

        var result = await NewService().CancelSignUpAsync(s.Id, volunteer.UserId, callerUserId: volunteer.UserId);

        Assert.Equal(TrainingSessionSignupResult.SessionCancelled, result);
    }

    // ─── MarkAttendeesCompleteAsync ────────────────────────────────────────

    [Fact]
    public async Task MarkAttendeesCompleteAsync_EmptyRoster_NoAttendees()
    {
        // Empty list → NoAttendees. Pages post an empty submit when the
        // marker pre-flight clears the entire roster; we surface a
        // friendly result rather than silently succeeding.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId, Array.Empty<AttendeeMark>(), "Cleared the roster.");

        Assert.Equal(TrainingSessionMutationResult.NoAttendees, result);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_EmptyNotes_ValidationFailed()
    {
        // Decision Q5: markerNotes REQUIRED on the bulk path. The audit
        // trail distinguishes online vs manual only when every manual
        // mark carries a non-empty reason.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "");

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_WhitespaceNotes_ValidationFailed()
    {
        // Whitespace IS empty for the purposes of decision Q5.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "   ");

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_CallerNotAdminOrCoordinator_PermissionDenied()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, volunteer.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "I marked myself");

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_RosterMemberNotInOrg_ValidationFailed()
    {
        // Defense in depth: a marker can't forge training records for
        // strangers. A count-mismatch (org membership rows != marked ids)
        // refuses with ValidationFailed so the page can surface "this
        // volunteer isn't a member of this org".
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var inOrgVolunteer = TestData.Person(Factory, "Vicky", "V");
        var stranger = TestData.Person(Factory, "Stranger", "McNobody");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, inOrgVolunteer.UserId, org.Id, OrganizationRole.Volunteer);
        // Intentionally NO Membership row for stranger.
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, inOrgVolunteer.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, stranger.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[]
            {
                new AttendeeMark { PersonUserId = inOrgVolunteer.UserId, Attended = true },
                new AttendeeMark { PersonUserId = stranger.UserId, Attended = true },
            },
            "Trying to mark a non-member");

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_HappyPath_WithTrainingContent_WritesCompletionWithCoordinatorManualSource()
    {
        // The center case: session has TrainingContentId, the marker
        // marks attendance, AND the completion row is created with the
        // bulk manual source (CoordinatorManual, NOT CoordinatorManualSingle
        // — the source attribute distinguishes "from session" from "ad-hoc").
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "Walked Vicky through the material.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.True(attendee.Attended);

        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        Assert.Equal(TrainingCompletionSource.CoordinatorManual, completion.CompletionSource);
        Assert.Equal(coord.UserId, completion.MarkedCompleteByUserId);
        Assert.Equal("Walked Vicky through the material.", completion.ManualCompletionNotes);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_FlipsScheduledSessionToCompleted()
    {
        // Side-effect: marking attendance on a Scheduled session flips
        // status to Completed so the Index "Past" tab picks it up.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "Verified attendance.");

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(TrainingSessionStatus.Completed, session.Status);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_OnCancelledSession_LeavesSessionCancelled()
    {
        // SPEC: audit trail survives cancellation, but the session
        // status itself stays Cancelled. Marker can record attendance
        // post-cancellation; the session row's status doesn't flip back.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id,
            trainingContentId: content.Id,
            status: TrainingSessionStatus.Cancelled);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "Attendance recorded after cancellation.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(TrainingSessionStatus.Cancelled, session.Status);

        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        Assert.Equal(TrainingCompletionSource.CoordinatorManual, completion.CompletionSource);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_NoTrainingContentId_OnlyAttendedFlagSet()
    {
        // Free-form session with no linked TrainingContent → marker
        // just records the attendee's attended flag. No completion row
        // (no content to claim against).
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: null);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "General orientation: no specific training.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.True(attendee.Attended);
        Assert.Empty(await db.TrainingCompletions.ToListAsync());
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_NoShow_AttendedFalse_NoCompletionRow()
    {
        // Decision: no-shows recorded as attended=false. No completion
        // row written — the volunteer wasn't there, so the content
        // claim doesn't apply even if the session has TrainingContentId.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = false } },
            "No-show recorded.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.False(attendee.Attended);
        Assert.Empty(await db.TrainingCompletions.ToListAsync());
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_WalkInNotOnOriginalRoster_AddsAttendeeAndCompletes()
    {
        // Marker includes a user who wasn't on the original roster
        // (showed up after the fact). The service auto-adds them with
        // their Attended flag in a single insert AND writes the
        // completion row — the verification list doesn't lose the
        // audit trail for walk-ins.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var walkIn = TestData.Person(Factory, "Walker", "In");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, walkIn.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        // walker is in the org but NOT on the original attendee list.

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = walkIn.UserId, Attended = true } },
            "Walk-in attended for full session.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == walkIn.UserId);
        Assert.True(attendee.Attended);

        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == walkIn.UserId && c.TrainingContentId == content.Id);
        Assert.Equal(TrainingCompletionSource.CoordinatorManual, completion.CompletionSource);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_ExistingCompletion_OverwritesInPlace()
    {
        // Decision Q7: latest-wins on the bulk path too. An existing
        // completion (e.g. from prior session attendance OR from
        // engagement-verified RecordCompletionAsync) is overwritten in
        // place. Captures the most recent marker + notes.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);
        // Pre-existing user-online completion.
        TestData.Completion(Factory, volunteer.UserId, content.Id, DateTime.UtcNow);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "Bulk mark after online completion existed.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completions = await db.TrainingCompletions
            .Where(c => c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id)
            .ToListAsync();
        Assert.Single(completions);
        Assert.Equal(TrainingCompletionSource.CoordinatorManual, completions[0].CompletionSource);
        Assert.Equal(coord.UserId, completions[0].MarkedCompleteByUserId);
        Assert.Equal("Bulk mark after online completion existed.", completions[0].ManualCompletionNotes);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_MultipleAttendees_AllMarkedIndependently()
    {
        // Three volunteers on the roster, mixed attended/no-show. Each
        // row gets the right attended flag AND each attended=true gets
        // their own completion row.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var v1 = TestData.Person(Factory, "Vicky1", "V");
        var v2 = TestData.Person(Factory, "Vicky2", "V");
        var v3 = TestData.Person(Factory, "Vicky3", "V");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, v1.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v2.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v3.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, v1.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, v2.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, v3.UserId);

        var result = await NewService().MarkAttendeesCompleteAsync(
            s.Id, coord.UserId,
            new[]
            {
                new AttendeeMark { PersonUserId = v1.UserId, Attended = true },
                new AttendeeMark { PersonUserId = v2.UserId, Attended = false }, // no-show
                new AttendeeMark { PersonUserId = v3.UserId, Attended = true },
            },
            "Two attended, one no-show.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        // Two attended=true → two completion rows (one each for v1, v3).
        var completions = await db.TrainingCompletions.ToListAsync();
        Assert.Equal(2, completions.Count);
        Assert.Contains(completions, c => c.PersonUserId == v1.UserId);
        Assert.DoesNotContain(completions, c => c.PersonUserId == v2.UserId);
        Assert.Contains(completions, c => c.PersonUserId == v3.UserId);
    }

    [Fact]
    public async Task MarkAttendeesCompleteAsync_UnknownSessionId_NotFound()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().MarkAttendeesCompleteAsync(
            999_999, coord.UserId,
            new[] { new AttendeeMark { PersonUserId = volunteer.UserId, Attended = true } },
            "Bogus session id");

        Assert.Equal(TrainingSessionMutationResult.NotFound, result);
    }

    // ─── Round-FR-2.2 polish: IDOR defense + capacity recheck ────────

    [Fact]
    public async Task SignUpAsync_CallerIsNotPerson_NotFound()
    {
        // IDOR defense: a page handler (or a malicious form) can't sign
        // up a stranger under the volunteer's account. The service is
        // the security boundary and refuses cross-user sign-up attempts
        // before any membership/duplicate check runs.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        var attacker = TestData.Person(Factory, "Attacker", "McHostile");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        // attacker is also an org member — they're a legitimate user
        // trying to sign someone else up. The service still refuses.
        TestData.Membership(Factory, attacker.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().SignUpAsync(s.Id, volunteer.UserId, callerUserId: attacker.UserId);

        Assert.Equal(TrainingSessionSignupResult.NotFound, result);

        // Defense in depth: no attendee row was inserted.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.TrainingSessionAttendees.AnyAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId));
    }

    [Fact]
    public async Task CancelSignUpAsync_CallerIsNotPerson_NotFound()
    {
        // IDOR defense (matches SignUpAsync): a page can't cancel
        // someone else's sign-up. The service refuses the cross-user
        // request before any attendee lookup runs.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        var attacker = TestData.Person(Factory, "Attacker", "McHostile");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, attacker.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().CancelSignUpAsync(s.Id, volunteer.UserId, callerUserId: attacker.UserId);

        Assert.Equal(TrainingSessionSignupResult.NotFound, result);

        // Defense in depth: the attendee row was NOT removed.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.TrainingSessionAttendees.AnyAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId));
    }

    [Fact]
    public async Task EditAsync_ShrinkMaxAttendeesBelowCurrentRoster_ValidationFailed()
    {
        // Capacity-vs-roster recheck: shrinking MaxAttendees below the
        // current attendee count would leave the session over-committed.
        // The service refuses the edit explicitly so a coord can't push
        // an over-filled session out the door.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var v1 = TestData.Person(Factory, "Vicky1", "V");
        var v2 = TestData.Person(Factory, "Vicky2", "V");
        var v3 = TestData.Person(Factory, "Vicky3", "V");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, v1.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v2.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v3.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, maxAttendees: 10);
        TestData.TrainingSessionAttendee(Factory, s.Id, v1.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, v2.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, v3.UserId);
        // Now try to shrink to 2 (3 currently signed up).
        var newStart = DateTime.UtcNow.AddDays(14);
        var result = await NewService().EditAsync(
            s.Id, "Shrunk", null, "Same room", newStart, newStart.AddHours(1),
            maxAttendees: 2, trainingContentId: null, callerUserId: coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);

        // Defense in depth: the existing MaxAttendees is unchanged.
        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(10, session.MaxAttendees);
    }

    [Fact]
    public async Task EditAsync_RaiseMaxAttendeesAboveCurrentRoster_Succeeds()
    {
        // Companion to the recheck test: raising the cap (or setting it
        // to null = unlimited) is always allowed — the recheck only
        // refuses shrinking below the current count.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var v1 = TestData.Person(Factory, "Vicky1", "V");
        var v2 = TestData.Person(Factory, "Vicky2", "V");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, v1.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, v2.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, maxAttendees: 2);
        TestData.TrainingSessionAttendee(Factory, s.Id, v1.UserId);
        TestData.TrainingSessionAttendee(Factory, s.Id, v2.UserId);
        // Raise to 10 — should succeed.
        var newStart = DateTime.UtcNow.AddDays(14);
        var result = await NewService().EditAsync(
            s.Id, "Raised", null, "Same room", newStart, newStart.AddHours(1),
            maxAttendees: 10, trainingContentId: null, callerUserId: coord.UserId);

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);
    }

    // ─── Round-FR-2.2 polish-2: IDOR empty-callerUserId defense ────────
    // The prior code allowed an empty callerUserId to slip through the
    // cross-user gate (a page that forgot to pass the param could
    // silently sign up / cancel for any volunteer). Locking in the
    // NotFound outcome here so the bypass can't quietly return.

    [Fact]
    public async Task SignUpAsync_EmptyCallerUserId_NotFound()
    {
        // A page handler that forgot to pass callerUserId (or a
        // malicious form posting a hand-crafted request) cannot sign
        // up a volunteer under the volunteer's account. Empty
        // callerUserId is treated as "not authenticated" and refused.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().SignUpAsync(s.Id, volunteer.UserId, callerUserId: "");

        Assert.Equal(TrainingSessionSignupResult.NotFound, result);

        // Defense in depth: no row inserted.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.TrainingSessionAttendees.AnyAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId));
    }

    [Fact]
    public async Task CancelSignUpAsync_EmptyCallerUserId_NotFound()
    {
        // Mirror test on the cancel path — same IDOR defense.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().CancelSignUpAsync(s.Id, volunteer.UserId, callerUserId: "");

        Assert.Equal(TrainingSessionSignupResult.NotFound, result);

        // Defense in depth: row NOT removed.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.TrainingSessionAttendees.AnyAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId));
    }

    // ─── Round-FR-2.3 polish-3 follow-up (i): SetAttendedAsync ──────
    // Per-row single-row counterpart of MarkAttendeesCompleteAsync.
    // Same security boundary (admin/coordinator of the session's
    // org) + same audit-trail shape, but with the marker's actual
    // notes captured at the call site. Replaces the round-1 synthetic
    // "Per-row mark from session detail page" notes string.

    [Fact]
    public async Task SetAttendedAsync_AttendedTrue_WithNotes_WritesCompletionWithCoordinatorManualSingle()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true,
            notes: "Verified attendance at the door.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.True(attendee.Attended);

        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id);
        // Per-row single source — distinct from the bulk mark's
        // CoordinatorManual so the audit trail can distinguish
        // "session-attended bulk" from "per-row ad-hoc mark".
        Assert.Equal(TrainingCompletionSource.CoordinatorManualSingle, completion.CompletionSource);
        Assert.Equal(coord.UserId, completion.MarkedCompleteByUserId);
        Assert.Equal("Verified attendance at the door.", completion.ManualCompletionNotes);
    }

    [Fact]
    public async Task SetAttendedAsync_AttendedTrue_NoLinkedContent_OnlyAttendedFlagSet()
    {
        // Free-form session with no linked TrainingContent → marker
        // just records the attendee's attended flag. No completion
        // row (no content to claim against). Mirrors the bulk
        // MarkAttendeesCompleteAsync behavior on the same shape.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: null);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true,
            notes: "General orientation: notes not consulted when no content linked.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.True(attendee.Attended);
        Assert.Empty(await db.TrainingCompletions.ToListAsync());
    }

    [Fact]
    public async Task SetAttendedAsync_AttendedTrue_WithContent_NoNotes_ValidationFailed()
    {
        // Decision Q5 carries over from the bulk path: notes are
        // required when attended=true AND the session has linked
        // training content. The audit trail must distinguish online
        // completions from coordinator marks; missing notes can't
        // be that distinction. Notes are NOT consulted when the
        // session has no linked content (covered in the prior test).
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true, notes: "");

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);

        // Defense in depth: Attended flag is NOT set when the call
        // is refused — the page can show a hint and let the user
        // retry with notes without leaving the session in a half-
        // marked state.
        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.Null(attendee.Attended);
        Assert.Empty(await db.TrainingCompletions.ToListAsync());
    }

    [Fact]
    public async Task SetAttendedAsync_AttendedFalse_NoCompletionRow()
    {
        // No-shows recorded as attended=false. No completion row
        // written — the volunteer wasn't there, so the content
        // claim doesn't apply even if the session has
        // TrainingContentId. Mirrors the bulk method's behavior.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: false,
            notes: "Did not show up.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.False(attendee.Attended);
        Assert.Empty(await db.TrainingCompletions.ToListAsync());
    }

    [Fact]
    public async Task SetAttendedAsync_WalkIn_NotOnRoster_AutoAdded()
    {
        // Marker records attendance for a volunteer who wasn't on
        // the original roster (showed up after the fact). The new
        // single-row service auto-adds them in the same call so
        // the marker doesn't have to do a two-step "add then mark"
        // flow (the walk-in card in the page handles the same path
        // via this method now too).
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var walkIn = TestData.Person(Factory, "Walker", "In");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, walkIn.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        // walkIn is in the org but NOT on the original attendee list.

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, walkIn.UserId, attended: true,
            notes: "Walk-in attended full session.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == walkIn.UserId);
        Assert.True(attendee.Attended);

        var completion = await db.TrainingCompletions.SingleAsync(c =>
            c.PersonUserId == walkIn.UserId && c.TrainingContentId == content.Id);
        Assert.Equal(TrainingCompletionSource.CoordinatorManualSingle, completion.CompletionSource);
    }

    [Fact]
    public async Task SetAttendedAsync_PlainVolunteerCaller_PermissionDenied()
    {
        // Only Admin/Coordinator of the session's org can mark.
        // A volunteer who happens to be on the roster can't mark
        // themselves (the round-1 "engagement-gate bypass" only
        // applies to qualified markers).
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, volunteer.UserId, volunteer.UserId, attended: true, notes: "Self-mark");

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task SetAttendedAsync_CoordinatorOfOtherOrg_PermissionDenied()
    {
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var coordB = TestData.Person(Factory, "Chris", "Coord");
        var volunteerA = TestData.Person(Factory, "Vicky", "V");
        TestData.Membership(Factory, coordB.UserId, orgB.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteerA.UserId, orgA.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, orgA.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteerA.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coordB.UserId, volunteerA.UserId, attended: true, notes: "Cross-org");

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task SetAttendedAsync_PersonNotInOrg_ValidationFailed()
    {
        // Defense in depth: a marker can't forge training records
        // for someone who isn't an org member. The marker's
        // authority is org-scoped; the marked volunteer's org
        // membership is checked too.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var stranger = TestData.Person(Factory, "Stranger", "McNobody");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        // Intentionally NO Membership row for stranger.
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, stranger.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, stranger.UserId, attended: true, notes: "Bogus mark");

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task SetAttendedAsync_EmptyCallerUserId_PermissionDenied()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, "", volunteer.UserId, attended: true, notes: "Empty caller");

        Assert.Equal(TrainingSessionMutationResult.PermissionDenied, result);
    }

    [Fact]
    public async Task SetAttendedAsync_EmptyPersonUserId_ValidationFailed_PersonNotInOrg()
    {
        // Renamed in polish-4 (was ...ValidationFailed) — empty personUserId fails the membership check, not the auth check.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        var s = TestData.TrainingSession(Factory, org.Id);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, "", attended: true, notes: "Empty person");

        Assert.Equal(TrainingSessionMutationResult.ValidationFailed, result);
    }

    [Fact]
    public async Task SetAttendedAsync_CancelledSession_AlreadyCancelled()
    {
        // Terminal-state guard matches MarkAttendeesCompleteAsync.
        // The marker can record attendance on a scheduled session
        // only.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, status: TrainingSessionStatus.Cancelled);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true, notes: "Post-cancel mark");

        Assert.Equal(TrainingSessionMutationResult.AlreadyCancelled, result);
    }

    [Fact]
    public async Task SetAttendedAsync_CompletedSession_AlreadyCompleted()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, status: TrainingSessionStatus.Completed);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true, notes: "Post-complete mark");

        Assert.Equal(TrainingSessionMutationResult.AlreadyCompleted, result);
    }

    [Fact]
    public async Task SetAttendedAsync_UnknownSessionId_NotFound()
    {
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewService().SetAttendedAsync(
            999_999, coord.UserId, volunteer.UserId, attended: true, notes: "Bogus session");

        Assert.Equal(TrainingSessionMutationResult.NotFound, result);
    }

    [Fact]
    public async Task SetAttendedAsync_DoesNotFlipSessionStatusToCompleted()
    {
        // Per the design: SetAttendedAsync is an INTERIM update for
        // a single row. The bulk MarkAttendeesCompleteAsync is what
        // flips session.Status to Completed (it finalizes the
        // attendance). Per-row marks leave the session Scheduled so
        // the coord can keep marking volunteers one by one.
        var org = TestData.Org(Factory, "Org A");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true, notes: "Per-row mark");

        await using var db = await Factory.CreateDbContextAsync();
        var session = await db.TrainingSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(TrainingSessionStatus.Scheduled, session.Status);
    }

    [Fact]
    public async Task SetAttendedAsync_ExistingCompletion_OverwritesInPlace()
    {
        // Latest-wins (decision Q7) carries over from the bulk path.
        // An existing user-online completion is overwritten in
        // place with the per-row manual mark.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);
        // Pre-existing user-online completion.
        TestData.Completion(Factory, volunteer.UserId, content.Id, DateTime.UtcNow);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: true,
            notes: "Per-row mark after online completion existed.");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var completions = await db.TrainingCompletions
            .Where(c => c.PersonUserId == volunteer.UserId && c.TrainingContentId == content.Id)
            .ToListAsync();
        Assert.Single(completions);
        Assert.Equal(TrainingCompletionSource.CoordinatorManualSingle, completions[0].CompletionSource);
        Assert.Equal(coord.UserId, completions[0].MarkedCompleteByUserId);
        Assert.Equal("Per-row mark after online completion existed.", completions[0].ManualCompletionNotes);
    }

    // ─── Round-FR-2.3 polish-3 follow-up (ii): ListMyScheduledSessionsAsync ──────
    // New service method that powers the MySchedule.razor training-event
    // feed wire-up. Returns Scheduled sessions in [fromUtc, toUtc) where
    // the supplied person is on the attendee list.

    [Fact]
    public async Task ListMyScheduledSessionsAsync_ReturnsScheduledSessionsInWindow_ForMyAttendeeRoster()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var inWindow = TestData.TrainingSession(Factory, org.Id, "In-window",
            startUtc: DateTime.UtcNow.AddDays(7), endUtc: DateTime.UtcNow.AddDays(7).AddHours(1));
        var outOfWindow = TestData.TrainingSession(Factory, org.Id, "Out-of-window",
            startUtc: DateTime.UtcNow.AddDays(60), endUtc: DateTime.UtcNow.AddDays(60).AddHours(1));
        var cancelled = TestData.TrainingSession(Factory, org.Id, "Cancelled session",
            startUtc: DateTime.UtcNow.AddDays(7), endUtc: DateTime.UtcNow.AddDays(7).AddHours(1),
            status: TrainingSessionStatus.Cancelled);
        var notMine = TestData.TrainingSession(Factory, org.Id, "Someone else's",
            startUtc: DateTime.UtcNow.AddDays(7), endUtc: DateTime.UtcNow.AddDays(7).AddHours(1));
        TestData.TrainingSessionAttendee(Factory, inWindow.Id, volunteer.UserId);
        TestData.TrainingSessionAttendee(Factory, outOfWindow.Id, volunteer.UserId);
        TestData.TrainingSessionAttendee(Factory, cancelled.Id, volunteer.UserId);
        // notMine: NOT on volunteer's roster.

        var fromUtc = DateTime.UtcNow;
        var toUtc = DateTime.UtcNow.AddDays(30);
        var result = await NewService().ListMyScheduledSessionsAsync(volunteer.UserId, fromUtc, toUtc);

        var session = Assert.Single(result);
        Assert.Equal("In-window", session.Title);
    }

    [Fact]
    public async Task ListMyScheduledSessionsAsync_EmptyPersonUserId_ReturnsEmpty()
    {
        var org = TestData.Org(Factory, "Org A");
        var session = TestData.TrainingSession(Factory, org.Id);
        // No attendees — should not matter; empty userId short-circuits.

        var result = await NewService().ListMyScheduledSessionsAsync(
            "", DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListMyScheduledSessionsAsync_PersonNotOnAnyRoster_ReturnsEmpty()
    {
        var org = TestData.Org(Factory, "Org A");
        TestData.TrainingSession(Factory, org.Id);
        var bystander = TestData.Person(Factory, "Bystander", "McNobody");

        var result = await NewService().ListMyScheduledSessionsAsync(
            bystander.UserId, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListMyScheduledSessionsAsync_OrdersByStartUtc_Ascending()
    {
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        // Insert in non-chronological order to verify the sort.
        var third = TestData.TrainingSession(Factory, org.Id, "Third",
            startUtc: DateTime.UtcNow.AddDays(20), endUtc: DateTime.UtcNow.AddDays(20).AddHours(1));
        var first = TestData.TrainingSession(Factory, org.Id, "First",
            startUtc: DateTime.UtcNow.AddDays(5), endUtc: DateTime.UtcNow.AddDays(5).AddHours(1));
        var second = TestData.TrainingSession(Factory, org.Id, "Second",
            startUtc: DateTime.UtcNow.AddDays(10), endUtc: DateTime.UtcNow.AddDays(10).AddHours(1));
        TestData.TrainingSessionAttendee(Factory, third.Id, volunteer.UserId);
        TestData.TrainingSessionAttendee(Factory, first.Id, volunteer.UserId);
        TestData.TrainingSessionAttendee(Factory, second.Id, volunteer.UserId);

        var fromUtc = DateTime.UtcNow;
        var toUtc = DateTime.UtcNow.AddDays(30);
        var result = await NewService().ListMyScheduledSessionsAsync(volunteer.UserId, fromUtc, toUtc);

        Assert.Equal(3, result.Count);
        Assert.Equal("First", result[0].Title);
        Assert.Equal("Second", result[1].Title);
        Assert.Equal("Third", result[2].Title);
    }

    [Fact]
    public async Task SetAttendedAsync_AttendedFalse_EmptyNotes_Succeeds()
    {
        // Pins the design: notes are NOT consulted for attended=false
        // (no completion row is written, so the audit-trail distinction
        // doesn't apply). The bulk path requires notes for any
        // submission; the per-row single method is more lenient because
        // each row stands alone. Future refactors that tighten this must
        // update this test.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var coord = TestData.Person(Factory, "Chris", "Coord");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, coord.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var s = TestData.TrainingSession(Factory, org.Id, trainingContentId: content.Id);
        TestData.TrainingSessionAttendee(Factory, s.Id, volunteer.UserId);

        var result = await NewService().SetAttendedAsync(
            s.Id, coord.UserId, volunteer.UserId, attended: false, notes: "");

        Assert.Equal(TrainingSessionMutationResult.Succeeded, result);

        await using var db = await Factory.CreateDbContextAsync();
        var attendee = await db.TrainingSessionAttendees.SingleAsync(a =>
            a.TrainingSessionId == s.Id && a.PersonUserId == volunteer.UserId);
        Assert.False(attendee.Attended);
        Assert.Empty(await db.TrainingCompletions.ToListAsync());
    }

    [Fact]
    public async Task ListMyScheduledSessionsAsync_WindowInclusivity_FromUtcInclusiveToUtcExclusive()
    {
        // Pins the half-open [fromUtc, toUtc) window: a session starting
        // exactly at toUtc is excluded; a session starting exactly at
        // fromUtc is included. Future refactors that change either
        // boundary must update this test.
        var org = TestData.Org(Factory, "Org A");
        var volunteer = TestData.Person(Factory, "Vicky", "Volunteer");
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        var fromUtc = DateTime.UtcNow.AddDays(10);
        var toUtc = DateTime.UtcNow.AddDays(20);
        var atFrom = TestData.TrainingSession(Factory, org.Id, "At fromUtc (inclusive)",
            startUtc: fromUtc, endUtc: fromUtc.AddHours(1));
        var atTo = TestData.TrainingSession(Factory, org.Id, "At toUtc (exclusive)",
            startUtc: toUtc, endUtc: toUtc.AddHours(1));
        TestData.TrainingSessionAttendee(Factory, atFrom.Id, volunteer.UserId);
        TestData.TrainingSessionAttendee(Factory, atTo.Id, volunteer.UserId);

        var result = await NewService().ListMyScheduledSessionsAsync(volunteer.UserId, fromUtc, toUtc);

        var session = Assert.Single(result);
        Assert.Equal("At fromUtc (inclusive)", session.Title);
        // Explicit half-open boundary assertion: toUtc is excluded.
        // Without this line, a refactor that flipped both bounds to
        // inclusive (or both to exclusive) would still pass on the
        // "at fromUtc is included" assertion alone.
        Assert.DoesNotContain(result, s => s.Title == "At toUtc (exclusive)");
    }
}
