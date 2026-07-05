using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="GameService"/>.
/// Covers the arena-conflict matrix (touching, overlapping, conflict-with-
/// cancelled/postponed, self-exclusion on update) and the cross-ministry /
/// cross-org scope checks.
/// </summary>
public class GameServiceTests : SqliteTestBase
{
    private static readonly DateTime Now = new(2026, 7, 11, 9, 0, 0, DateTimeKind.Utc);

    private GameService NewService() => new(Factory);

    private record Fixture(
        Organization Org,
        Organization OtherOrg,
        Ministry League,
        Ministry OtherLeague,
        Team Home,
        Team Away,
        Team OtherLeagueTeam,
        Arena Field1,
        Arena OtherOrgArena);

    private async Task<Fixture> BuildFixtureAsync()
    {
        var org = TestData.Org(Factory, "Test Org");
        var otherOrg = TestData.Org(Factory, "Other Org");
        var league = TestData.Ministry(Factory, org.Id, "Soccer League");
        var otherLeague = TestData.Ministry(Factory, otherOrg.Id, "Other League");
        var home = TestData.Team(Factory, league.Id, "Eagles");
        var away = TestData.Team(Factory, league.Id, "Hawks");
        var otherLeagueTeam = TestData.Team(Factory, otherLeague.Id, "Comets");
        var field1 = TestData.Arena(Factory, org.Id, "Field 1");
        var otherArena = TestData.Arena(Factory, otherOrg.Id, "Other Field");
        return new Fixture(org, otherOrg, league, otherLeague, home, away, otherLeagueTeam, field1, otherArena);
    }

    // ---- happy path ----

    [Fact]
    public async Task ScheduleGame_ValidGame_ReturnsOk()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.NotNull(result.Game);
        Assert.Equal(f.Home.Id, result.Game!.HomeTeamId);
    }

    // ---- input validation ----

    [Fact]
    public async Task ScheduleGame_EndBeforeStart_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now.AddHours(1), Now);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("End time must be after start time"));
    }

    [Fact]
    public async Task ScheduleGame_SameHomeAndAway_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ScheduleGameAsync(
            ministryId: f.League.Id, homeTeamId: f.Home.Id, awayTeamId: f.Home.Id, arenaId: f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("must be different"));
    }

    [Fact]
    public async Task ScheduleGame_TeamsInDifferentLeagues_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // home = league team, away = other-league team → ministryId mismatch.
        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.OtherLeagueTeam.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("same league"));
    }

    [Fact]
    public async Task ScheduleGame_ArenaInDifferentOrg_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.OtherOrgArena.Id,
            Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("same organization"));
    }

    // ---- arena-conflict matrix ----

    [Fact]
    public async Task ScheduleGame_OverlappingExistingAtSameArena_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing 9:00–10:00, candidate 9:30–10:30 → overlap 9:30–10:00.
        TestData.Game(Factory, f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now.AddMinutes(30), Now.AddHours(1).AddMinutes(30));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Arena is already booked"));
    }

    [Fact]
    public async Task ScheduleGame_TouchingBoundaryIsAllowed()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing 9:00–10:00, candidate 10:00–11:00. Touching is allowed
        // (existing.End == candidate.Start is NOT a conflict per the strict
        // inequality: existing.Start < candidate.End AND existing.End > candidate.Start).
        TestData.Game(Factory, f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now.AddHours(1), Now.AddHours(2));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task ScheduleGame_CancelledExistingIsIgnored()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Existing 9:00–10:00 is Cancelled — should be ignored.
        TestData.Game(Factory, f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1), status: GameStatus.Cancelled);

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task ScheduleGame_PostponedExistingIsIgnored()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Postponed is also a non-terminal state that shouldn't block.
        TestData.Game(Factory, f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1), status: GameStatus.Postponed);

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task ScheduleGame_DifferentArenaSameTime_IsAllowed()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        var field2 = TestData.Arena(Factory, f.Org.Id, "Field 2");

        TestData.Game(Factory, f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        var result = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, field2.Id,
            Now, Now.AddHours(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    // ---- update / score ----

    [Fact]
    public async Task UpdateGame_ValidUpdate_ExcludesSelfFromConflict()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        // Schedule one, then update it in place — its own time shouldn't
        // conflict with itself.
        var first = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        var result = await svc.UpdateGameAsync(
            first.Game!.Id, f.Away.Id, f.Home.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task UpdateGame_OverlappingDifferentGameAtSameArena_Fails()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var first = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));
        var second = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now.AddHours(2), Now.AddHours(3));

        // Try to move the second game onto the first's window.
        var result = await svc.UpdateGameAsync(
            second.Game!.Id, f.Away.Id, f.Home.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Arena is already booked"));
    }

    [Fact]
    public async Task SetScore_UpdatesScoreAndMarksAsPlayed()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();

        var scheduled = await svc.ScheduleGameAsync(
            f.League.Id, f.Home.Id, f.Away.Id, f.Field1.Id,
            Now, Now.AddHours(1));

        var played = await svc.SetScoreAsync(scheduled.Game!.Id, 3, 1);

        Assert.Equal(3, played.HomeScore);
        Assert.Equal(1, played.AwayScore);
        Assert.Equal(GameStatus.Played, played.Status);
    }
}
