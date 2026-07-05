using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Round M tests for the engagement-verification feature added to
/// <see cref="TrainingService"/>: <c>SyncActivityAsync</c> writes a
/// per-user <see cref="TrainingActivity"/> row, <c>CheckEligibilityAsync</c>
/// reports progress + unlocks <c>IsEligible</c> once the volunteer has
/// actually engaged, and <c>RecordCompletionAsync</c> refuses an
/// under-engaged volunteer with <see cref="TrainingCompletionResult.InsufficientEngagement"/>.
/// </summary>
public class TrainingActivityTests : SqliteTestBase
{
    private TrainingService NewService() => new TrainingService(Factory);

    // ─── SyncActivityAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SyncActivityAsync_FirstCall_CreatesRow()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 5, ActualDurationSec = 60 });

        await using var db = await Factory.CreateDbContextAsync();
        var activity = await db.TrainingActivities.SingleAsync();
        Assert.Equal(content.Id, activity.TrainingContentId);
        Assert.Equal(content.Version, activity.TrainingContentVersion);
        Assert.Equal(5, activity.HighestWatchedSec);
        Assert.Equal(60, activity.ActualDurationSec);
    }

    [Fact]
    public async Task SyncActivityAsync_HighestWatchedSecIsMonotonic_ClientCannotBurnDown()
    {
        // Anti-cheat invariant: a volunteer who watched 80s, then sends
        // 30s in a subsequent sync (e.g. resetting the player), still
        // gets an eligible HighestWatchedSec. Without Math.Max the server
        // would let them game the 95% rule by sending low values.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        await svc.SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 80 });
        await svc.SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 30 });

        await using var db = await Factory.CreateDbContextAsync();
        var activity = await db.TrainingActivities.SingleAsync();
        Assert.Equal(80, activity.HighestWatchedSec);
    }

    [Fact]
    public async Task SyncActivityAsync_ViewedPages_UnionWithoutDuplicates()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safe Spaces");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        await svc.SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = new[] { 1, 2, 3 } });
        await svc.SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = new[] { 3, 4, 5 } });

        await using var db = await Factory.CreateDbContextAsync();
        var activity = await db.TrainingActivities.SingleAsync();
        var pages = activity.GetViewedPages();
        Assert.Equal(5, pages.Count);
        Assert.Contains(1, pages);
        Assert.Contains(5, pages);
    }

    [Fact]
    public async Task SyncActivityAsync_CallerNotInContentOrg_NoRowWritten()
    {
        // Volunteer in Org A tries to write into Org B's training
        // activity log. The org-membership gate runs before the upsert;
        // no row gets created so a hostile client can't pollute a
        // foreign org's records (or use them to later spoof
        // completion).
        var orgB = TestData.Org(Factory, "Org B");
        var content = TestData.TrainingContent(Factory, orgB.Id, "Secret Training");
        var user = TestData.Person(Factory);
        var orgA = TestData.Org(Factory, "Org A");
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 999 });

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.TrainingActivities.ToListAsync());
    }

    [Fact]
    public async Task SyncActivityAsync_UnknownContent_NoRowWritten()
    {
        // ContentId doesn't resolve → silently skip (the page handler
        // treats this as content-gone and surfaces the explanation on
        // completion call).
        var user = TestData.Person(Factory);

        await NewService().SyncActivityAsync(user.UserId, 999_999,
            new TrainingActivitySync { HighestWatchedSec = 5 });

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.TrainingActivities.ToListAsync());
    }

    // ─── CheckEligibilityAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckEligibilityAsync_Pdf_AllPagesViewed_IsEligible()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.PdfContent(Factory, org.Id, "PDF Guide", totalPages: 12);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = Enumerable.Range(1, 12).ToArray() });

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.True(snapshot.IsEligible);
        Assert.Equal(12, snapshot.TotalPages);
        Assert.Equal(12, snapshot.ViewedPagesCount);
    }

    [Fact]
    public async Task CheckEligibilityAsync_Pdf_PartialViewed_IsIneligibleWithReason()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.PdfContent(Factory, org.Id, "PDF Guide", totalPages: 12);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = new[] { 1, 2, 3 } });

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.False(snapshot.IsEligible);
        Assert.Contains("3 of 12", snapshot.Reason);
    }

    [Fact]
    public async Task CheckEligibilityAsync_Pdf_ZeroPagesUploaded_IsIneligibleDefensively()
    {
        // If TotalPageCount is null (e.g. admin uploaded before
        // PdfPageCounter was wired) the rule refuses to mark complete
        // rather than silently sweeping non-existent pages.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.PdfContent(Factory, org.Id, "PDF Guide", totalPages: null);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = new[] { 1 } });

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.False(snapshot.IsEligible);
        Assert.Contains("unknown", snapshot.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckEligibilityAsync_Video_FullDuration_IsEligible()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safety Video");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        // 200s video, watch 200s → 100% > 95% → eligible.
        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 200, ActualDurationSec = 200 });

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.True(snapshot.IsEligible);
    }

    [Fact]
    public async Task CheckEligibilityAsync_Video_Below95_IsIneligible()
    {
        // 100s video, watch 80s → 80% < 95% → ineligible.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safety Video");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 80, ActualDurationSec = 100 });

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.False(snapshot.IsEligible);
        Assert.Contains("80", snapshot.Reason);
    }

    [Fact]
    public async Task CheckEligibilityAsync_Slideshow_ShortDwell_IsIneligible()
    {
        // Slideshow / external URL: best-effort dwell timer must hit
        // 80% of EstimatedDuration before completion unlocks. We can't
        // fast-forward time inside the test, so we use a minute-long
        // EstimatedDuration and immediately sync — DwellSec near 0 is
        // not enough.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Slides");
        // Need to update EstimatedDuration directly so the test sees a
        // 60s requirement while keeping the fixture cheap.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var row = await db.TrainingContents.FindAsync(content.Id);
            row!.EstimatedDuration = TimeSpan.FromSeconds(60);
            row.Format = TrainingFormat.Slideshow;
            await db.SaveChangesAsync();
        }
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.False(snapshot.IsEligible);
    }

    [Fact]
    public async Task CheckEligibilityAsync_CallerNotInOrg_HasNoEligibility()
    {
        // Hard privacy/leak invariant — a non-member shouldn't be able
        // to learn anything about the training they're not entitled to
        // take. The reason text doesn't carry content details.
        var orgB = TestData.Org(Factory, "Org B");
        var content = TestData.PdfContent(Factory, orgB.Id, "Confidential", totalPages: 18);
        var user = TestData.Person(Factory);
        var orgA = TestData.Org(Factory, "Org A");
        TestData.Membership(Factory, user.UserId, orgA.Id, OrganizationRole.Volunteer);

        var snapshot = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.False(snapshot.IsEligible);
        Assert.Contains("not a member", snapshot.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ─── RecordCompletionAsync gate (InsufficientEngagement) ────────────────────

    [Fact]
    public async Task RecordCompletionAsync_PdfPartialEngagement_ReturnsInsufficientEngagement()
    {
        // Trust-boundary invariant: even if a hostile client jumps
        // straight to "Mark as completed" without first having the
        // JS bridge tick, the service refuses with the new enum value.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.PdfContent(Factory, org.Id, "PDF Guide", totalPages: 12);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        // Only page 1 — should be refused.
        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = new[] { 1 } });

        var result = await NewService().RecordCompletionAsync(user.UserId, content.Id, DateTime.UtcNow);
        Assert.Equal(TrainingCompletionResult.InsufficientEngagement, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.TrainingCompletions.AnyAsync(c =>
            c.PersonUserId == user.UserId && c.TrainingContentId == content.Id));
    }

    [Fact]
    public async Task RecordCompletionAsync_VideoBelow95_ReturnsInsufficientEngagement()
    {
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Safety Video");
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 80, ActualDurationSec = 100 });

        var result = await NewService().RecordCompletionAsync(user.UserId, content.Id, DateTime.UtcNow);
        Assert.Equal(TrainingCompletionResult.InsufficientEngagement, result);
    }

    [Fact]
    public async Task RecordCompletionAsync_PdfFullEngagement_ReturnsRecorded()
    {
        // Full engagement → no InsufficientEngagement, recorded as usual.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.PdfContent(Factory, org.Id, "PDF Guide", totalPages: 12);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Requirement(Factory, content.Id, orgId: org.Id);

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { ViewedPages = Enumerable.Range(1, 12).ToArray() });

        var result = await NewService().RecordCompletionAsync(user.UserId, content.Id, DateTime.UtcNow);
        Assert.Equal(TrainingCompletionResult.Recorded, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.TrainingCompletions.AnyAsync(c =>
            c.PersonUserId == user.UserId && c.TrainingContentId == content.Id));
    }

    [Fact]
    public async Task SyncActivityAsync_DisjointConcurrentPageSets_UnionSurvives()
    {
        // Round-M concurrent-update test: two near-simultaneous syncs
        // post disjoint page sets. Read-modify-write pattern would lose
        // one set; we expect the persisted ViewedPagesCsv to contain
        // BOTH sets after both complete. SQLite's per-connection
        // serializability gives us this today; if we ever switch to
        // Postgres, run this test to catch a race regression.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.PdfContent(Factory, org.Id, "PDF Guide", totalPages: 20);
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        var svc = NewService();
        await Task.WhenAll(
            svc.SyncActivityAsync(user.UserId, content.Id,
                new TrainingActivitySync { ViewedPages = new[] { 1, 2, 3, 4, 5 } }),
            svc.SyncActivityAsync(user.UserId, content.Id,
                new TrainingActivitySync { ViewedPages = new[] { 11, 12, 13, 14, 15 } }));

        await using var db = await Factory.CreateDbContextAsync();
        var pages = (await db.TrainingActivities.SingleAsync()).GetViewedPages();
        Assert.Contains(1, pages);
        Assert.Contains(5, pages);
        Assert.Contains(11, pages);
        Assert.Contains(15, pages);
        Assert.Equal(10, pages.Count);
    }

    [Fact]
    public async Task SyncActivityAsync_StaleSessionHoursLater_ReanchorsFirstOpened()
    {
        // Anti-abuse invariant: FirstOpenedUtc should NOT accumulate
        // dwell forever. A volunteer who opened this training two hours
        // ago (then closed the tab) and now re-engages must NOT instantly
        // qualify for a 60s Slideshow completion rule. We simulate the
        // stale session by writing LastUpdatedUtc far in the past.
        var org = TestData.Org(Factory, "Org A");
        var content = TestData.TrainingContent(Factory, org.Id, "Slides");
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var row = await db.TrainingContents.FindAsync(content.Id);
            row!.EstimatedDuration = TimeSpan.FromSeconds(60);
            row.Format = TrainingFormat.Slideshow;
            await db.SaveChangesAsync();
        }
        var user = TestData.Person(Factory);
        TestData.Membership(Factory, user.UserId, org.Id, OrganizationRole.Volunteer);

        // Seed an old activity row anchored 2 hours ago.
        long pastTicks = DateTime.UtcNow.AddHours(-2).Ticks;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            db.TrainingActivities.Add(new TrainingActivity
            {
                PersonUserId = user.UserId,
                TrainingContentId = content.Id,
                TrainingContentVersion = content.Version,
                ViewedPagesCsv = "",
                HighestWatchedSec = 0,
                ActualDurationSec = 0,
                FirstOpenedUtc = new DateTime(pastTicks, DateTimeKind.Utc),
                LastUpdatedUtc = new DateTime(pastTicks, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        await NewService().SyncActivityAsync(user.UserId, content.Id,
            new TrainingActivitySync { HighestWatchedSec = 5 });

        var snap = await NewService().CheckEligibilityAsync(user.UserId, content.Id);
        Assert.False(snap.IsEligible);
        // After re-anchor the dwell should be near 0, not 7200+.
        Assert.True(snap.DwellSec < 60, $"Expected new dwell window (< 60s) after stale-session reset; got {snap.DwellSec}s.");
    }
}
