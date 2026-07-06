using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Tests for the round-AY read helper
/// <see cref="ITeamService.ListActivePlayersWithContactsAsync"/>.
/// Pins the "active player" convention that
/// <c>Components/Pages/Teams/Detail.razor</c> already uses
/// (<c>LeftUtc == null || LeftUtc &gt; now</c>) so the query and the page
/// stay in lock-step. The page is the parent-facing roster table at
/// <c>/Teams/{TeamId}/Signups</c>; the gate is
/// <c>IsSystemAdminAsync || CanManageTeamAsync</c>. This file exercises
/// the query path only \u2014 gating is tested in OrgAuthServiceTests.
public class TeamServiceTests : SqliteTestBase
{
    private TeamService NewSvc() => new(Factory);

    [Fact]
    public async Task ListActivePlayersWithContactsAsync_ActiveFilter_FollowsTeamsDetailRazorConvention()
    {
        // Pin the canonical "active player" rule used elsewhere in the
        // app: a player is on the current roster iff LeftUtc is null OR
        // LeftUtc > DateTime.UtcNow. Players with a Past LeftUtc are
        // excluded from the contact surface because their parent info
        // may be stale. Players with a future-dated LeftUtc remain
        // visible today (the transition hasn't happened yet).
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id, "Referees");
        var team = TestData.Team(Factory, min.Id);

        var neverLeft = TestData.Player(Factory, team.Id, "Never", "Stayed");
        var futureTransition = TestData.Player(Factory, team.Id, "Future", "Move",
            leftUtc: DateTime.UtcNow.AddDays(30));
        var pastLeft = TestData.Player(Factory, team.Id, "Old", "Graduated",
            leftUtc: DateTime.UtcNow.AddDays(-1));
        var longPastLeft = TestData.Player(Factory, team.Id, "Way", "Old",
            leftUtc: DateTime.UtcNow.AddDays(-365));

        var list = await NewSvc().ListActivePlayersWithContactsAsync(team.Id);
        var ids = list.Select(p => p.Id).ToHashSet();

        Assert.Equal(2, list.Count);
        Assert.Contains(neverLeft.Id, ids);
        Assert.Contains(futureTransition.Id, ids);
        Assert.DoesNotContain(pastLeft.Id, ids);
        Assert.DoesNotContain(longPastLeft.Id, ids);
    }

    [Fact]
    public async Task ListActivePlayersWithContactsAsync_EagerLoadsPrimaryContactPerson()
    {
        // The page reads p.PrimaryContactPerson.DisplayName per row, so
        // the navigation MUST be eager-loaded \u2014 a no-Include call would
        // force the page's per-row navigation into either NRE (page
        // uses AsNoTracking through the service, so its own DbContext
        // doesn't re-issue lazy loads) or per-row round-trips. Pin that
        // both linked and unlinked players are handled correctly \u2014
        // unlinked players appear in the result with the navigation
        // null, since PrimaryContactPersonUserId is nullable by design.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var team = TestData.Team(Factory, min.Id);
        var parent = TestData.Person(Factory, "Pat", "Parent");
        TestData.Membership(Factory, parent.UserId, org.Id, OrganizationRole.Volunteer);
        var linked = TestData.Player(Factory, team.Id, "Linked", "Kid",
            primaryContactPersonUserId: parent.UserId,
            primaryContactPhone: "555-1234",
            primaryContactEmail: "parent@example.com");
        var unlinked = TestData.Player(Factory, team.Id, "Unlinked", "Kid");
        TestData.Player(Factory, team.Id, "BarePhone", "Only",
            primaryContactPersonUserId: null, primaryContactPhone: "555-9999");

        var list = await NewSvc().ListActivePlayersWithContactsAsync(team.Id);

        Assert.Equal(3, list.Count);

        var linkedRow = list.Single(p => p.Id == linked.Id);
        Assert.NotNull(linkedRow.PrimaryContactPerson);
        Assert.Equal("Pat Parent", linkedRow.PrimaryContactPerson!.DisplayName);
        Assert.Equal("parent@example.com", linkedRow.PrimaryContactEmail);        var unlinkedRow = list.Single(p => p.Id == unlinked.Id);
        Assert.Null(unlinkedRow.PrimaryContactPerson);
        Assert.Null(unlinkedRow.PrimaryContactPhone);

        // Denormalized phone-only path: page must render the phone cell
        // even without a Person link. The BarePhone player has no
        // PrimaryContactPersonUserId but does have a denormalized
        // PrimaryContactPhone — the page should surface that phone
        // without needing the Person link.
        var bare = list.First(p => p.FirstName == "BarePhone");
        Assert.Null(bare.PrimaryContactPerson);
        Assert.Equal("555-9999", bare.PrimaryContactPhone);
    }

    [Fact]
    public async Task ListActivePlayersWithContactsAsync_SortedByJerseyThenName()
    {
        // Stable UI order: jersey ascending (nulls LAST so the real
        // roster comes first), then LastName ascending, then FirstName
        // ascending. Combined with the OrderBy-JerseyNumber.HasValue
        // trick the impl uses, players without a jersey sort after the
        // jerseyed players regardless of name.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var team = TestData.Team(Factory, min.Id);

        // Insert out of order to prove the ORDER BY is what's producing
        // the sort (not the insert order).
        TestData.Player(Factory, team.Id, "Zara", "Zenith", jerseyNumber: 7);
        TestData.Player(Factory, team.Id, "Mia", "Alpha", jerseyNumber: 7);  // tie: Mia < Zara by FirstName
        TestData.Player(Factory, team.Id, "Aaron", "Alpha", jerseyNumber: 7); // first in jersey=7 tie
        TestData.Player(Factory, team.Id, "Bob", "Beta", jerseyNumber: null); // nulls sort last
        TestData.Player(Factory, team.Id, "Carol", "Delta", jerseyNumber: 1);
        TestData.Player(Factory, team.Id, "Karl", "Yankee", jerseyNumber: 99);

        var list = await NewSvc().ListActivePlayersWithContactsAsync(team.Id);

        // First three should be in jersey order: Carol (1), then the
        // three jersey=7 players sorted by LastName (Alpha, Alpha) then
        // FirstName (Aaron, Mia), then Karl (99), then Bob (null jersey,
        // sorts last).
        Assert.Equal(
            new[] { "Carol", "Aaron", "Mia", "Zara", "Karl", "Bob" },
            list.Select(p => p.FirstName).ToArray());
        // Verify the jersey=7 alphabetic tie-break separately so a
        // future refactor that drops then-by-FirstName is caught.
        // Three players share jersey=7: Aaron Alpha, Mia Alpha, Zara
        // Zenith. Order is LastName ASC then FirstName ASC: Alpha
        // (Aaron), Alpha (Mia), Zenith (Zara).
        var jerseySeven = list.Where(p => p.JerseyNumber == 7).ToList();
        Assert.Equal(new[] { "Aaron", "Mia", "Zara" }, jerseySeven.Select(p => p.FirstName).ToArray());
        // Sanity: the jersey-null player really is at the end.
        Assert.Null(list[^1].JerseyNumber);
    }

    [Fact]
    public async Task ListActivePlayersWithContactsAsync_EmptyAndInvalidInputs_ReturnEmpty()
    {
        // Same defensive-empty contract as the page's other list
        // methods (ListForMinistryAsync): a non-existent teamId,
        // a zero/negative teamId, OR a real teamId with no players
        // all return an empty list rather than throwing.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var emptyTeam = TestData.Team(Factory, min.Id); // no players added

        Assert.Empty(await NewSvc().ListActivePlayersWithContactsAsync(emptyTeam.Id));
        Assert.Empty(await NewSvc().ListActivePlayersWithContactsAsync(99_999));
        Assert.Empty(await NewSvc().ListActivePlayersWithContactsAsync(0));
        Assert.Empty(await NewSvc().ListActivePlayersWithContactsAsync(-1));
    }

    [Fact]
    public async Task ListActivePlayersWithContactsAsync_AllPlayersLeft_ReturnsEmpty()
    {
        // "All-graduated" squad: no remaining current players, so the
        // parent-contact surface is empty. (Complement to the test
        // above which only covered a team with zero Players rows ever
        // added.) The Where clause must reject every LeftUtc-in-the-
        // past row so a future refactor that silently drops the
        // filter surfaces this case loudly.
        var org = TestData.Org(Factory);
        var min = TestData.Ministry(Factory, org.Id);
        var team = TestData.Team(Factory, min.Id);
        TestData.Player(Factory, team.Id, "First", "Graduated",
            leftUtc: DateTime.UtcNow.AddDays(-10));
        TestData.Player(Factory, team.Id, "Second", "Graduated",
            leftUtc: DateTime.UtcNow.AddDays(-200));
        TestData.Player(Factory, team.Id, "Third", "Graduated",
            leftUtc: DateTime.UtcNow.AddYears(-1));

        var list = await NewSvc().ListActivePlayersWithContactsAsync(team.Id);
        Assert.Empty(list);
    }
}
