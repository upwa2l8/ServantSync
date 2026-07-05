using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

public class StandingsCalculatorTests
{
    // A "now" that lets us treat most past games as already-played without
    // floating into 9999-12-31. All games in tests are stamped before this.
    private static readonly DateTime AsOf = new(2026, 7, 4, 22, 0, 0, DateTimeKind.Utc);

    private static Team Team(int id, string name) =>
        new() { Id = id, MinistryId = 1, Name = name, AgeBracket = TeamAgeBracket.U10 };

    private static Game Game(int id, int homeId, int awayId, int? homeScore, int? awayScore, GameStatus status = GameStatus.Played)
    {
        var start = AsOf.AddDays(-7);
        return new Game
        {
            Id = id,
            MinistryId = 1,
            HomeTeamId = homeId,
            AwayTeamId = awayId,
            ArenaId = 1,
            StartUtc = start,
            EndUtc = start.AddHours(1),
            Status = status,
            HomeScore = homeScore,
            AwayScore = awayScore,
        };
    }

    [Fact]
    public void Empty_Input_Returns_Empty_Standings()
    {
        var result = StandingsCalculator.Calculate(
            Array.Empty<Game>(),
            new[] { Team(1, "Eagles"), Team(2, "Hawks") },
            asOfUtc: AsOf);
        Assert.Equal(2, result.Count);
        Assert.All(result, s =>
        {
            Assert.Equal(0, s.Played);
            Assert.Equal(0, s.Points);
        });
    }

    [Fact]
    public void Single_Home_Win_Awards_3_Points_And_Updates_Goal_Diff()
    {
        var teams = new[] { Team(1, "Eagles"), Team(2, "Hawks") };
        var games = new[] { Game(100, homeId: 1, awayId: 2, homeScore: 3, awayScore: 1) };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);

        var eagles = s.Single(x => x.TeamName == "Eagles");
        var hawks = s.Single(x => x.TeamName == "Hawks");
        Assert.Equal(1, eagles.Played);
        Assert.Equal(1, eagles.Wins);
        Assert.Equal(0, eagles.Losses);
        Assert.Equal(3, eagles.GoalsFor);
        Assert.Equal(1, eagles.GoalsAgainst);
        Assert.Equal(2, eagles.GoalDifference);
        Assert.Equal(3, eagles.Points);

        Assert.Equal(0, hawks.Points);
        Assert.Equal(-2, hawks.GoalDifference);
        Assert.Equal(1, hawks.Losses);
    }

    [Fact]
    public void Draw_Awards_1_Point_Each_And_Zero_Goal_Diff()
    {
        var teams = new[] { Team(1, "Eagles"), Team(2, "Hawks") };
        var games = new[] { Game(100, 1, 2, homeScore: 2, awayScore: 2) };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);

        var eagles = s.Single(x => x.TeamName == "Eagles");
        var hawks = s.Single(x => x.TeamName == "Hawks");
        Assert.Equal(1, eagles.Draws);
        Assert.Equal(1, hawks.Draws);
        Assert.Equal(1, eagles.Points);
        Assert.Equal(1, hawks.Points);
        Assert.Equal(0, eagles.GoalDifference);
    }

    [Fact]
    public void Multiple_Games_For_Same_Team_Accumulate()
    {
        var teams = new[] { Team(1, "Eagles"), Team(2, "Hawks") };
        var games = new[]
        {
            Game(100, 1, 2, 2, 0),  // Eagles home 2-0 (Eagles win)
            Game(101, 2, 1, 1, 1),  // Hawks home 1-1 (draw; both teams score 1 as away)
        };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);
        var eagles = s.Single(x => x.TeamName == "Eagles");
        var hawks = s.Single(x => x.TeamName == "Hawks");

        // Eagles: game 100 scores 2 (home) + 1 (away) = 3 GF; concedes 0 (home) + 1 (away) = 1 GA.
        Assert.Equal(2, eagles.Played);
        Assert.Equal(1, eagles.Wins);
        Assert.Equal(1, eagles.Draws);
        Assert.Equal(4, eagles.Points);  // 3 + 1
        Assert.Equal(3, eagles.GoalsFor);  // 2 + 1
        Assert.Equal(1, eagles.GoalsAgainst);  // 0 + 1

        // Hawks: game 100 scores 0 (away) + 1 (home) = 1 GF; concedes 2 (away) + 1 (home) = 3 GA.
        Assert.Equal(0, hawks.Wins);
        Assert.Equal(1, hawks.Draws);
        Assert.Equal(1, hawks.Losses);
        Assert.Equal(1, hawks.Points);
    }

    [Fact]
    public void Cancelled_And_Postponed_Games_Are_Excluded()
    {
        var teams = new[] { Team(1, "Eagles"), Team(2, "Hawks") };
        var games = new[]
        {
            Game(100, 1, 2, 5, 0, GameStatus.Cancelled),
            Game(101, 1, 2, 5, 0, GameStatus.Postponed),
            Game(102, 1, 2, 2, 1),  // counts
        };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);
        var eagles = s.Single(x => x.TeamName == "Eagles");
        Assert.Equal(1, eagles.Played);
        Assert.Equal(1, eagles.Wins);
        Assert.Equal(3, eagles.Points);
    }

    [Fact]
    public void Forfeit_Status_Is_Excluded_From_Standings()
    {
        // Forfeits are tracked as Game rows but don't count toward W/L/D.
        // The league enters a final score on a separate "Played" row to
        // record the result, or handles it out-of-band.
        var teams = new[] { Team(1, "Eagles"), Team(2, "Hawks") };
        var games = new[]
        {
            Game(100, 1, 2, null, null, GameStatus.Forfeit),
        };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);
        Assert.All(s, x => Assert.Equal(0, x.Played));
    }

    [Fact]
    public void Points_Tie_Is_Broken_By_Goal_Difference()
    {
        var teams = new[] { Team(1, "Eagles"), Team(2, "Hawks") };
        var games = new[]
        {
            // Both teams win 1 game → 3 points each.
            // Eagles: GF 3+0=3, GA 0+1=1, GD +2.
            // Hawks:  GF 0+1=1, GA 3+0=3, GD -2.
            // Eagles should rank first on goal difference.
            Game(100, 1, 2, 3, 0),  // Eagles home 3-0
            Game(101, 2, 1, 1, 0),  // Hawks home 1-0
        };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);
        Assert.Equal(3, s[0].Points);
        Assert.Equal(3, s[1].Points);
        Assert.Equal("Eagles", s[0].TeamName);
        Assert.Equal("Hawks", s[1].TeamName);
        Assert.Equal(2, s[0].GoalDifference);
        Assert.Equal(-2, s[1].GoalDifference);
    }

    [Fact]
    public void Points_Tie_With_Equal_GD_Is_Broken_By_Goals_Scored_Then_Name()
    {
        var teams = new[]
        {
            Team(1, "Hawks"),
            Team(2, "Eagles"),
            Team(3, "Comets"),
        };
        var games = new[]
        {
            // All 3 teams have 1 win, 0 losses, 3 points.
            // Eagles 2-0 (GF 2, GD +2), Hawks 1-0 (GF 1, GD +1),
            // Comets 0-0 (impossible — force a synthetic result).
            // Use a 3-team round where every team wins 2-1.
            Game(100, 1, 2, 2, 1),  // Hawks 2-1 Eagles
            Game(101, 2, 3, 2, 1),  // Eagles 2-1 Comets
            Game(102, 3, 1, 2, 1),  // Comets 2-1 Hawks
        };

        var s = StandingsCalculator.Calculate(games, teams, asOfUtc: AsOf);
        Assert.All(s, x =>
        {
            Assert.Equal(2, x.Played);
            Assert.Equal(1, x.Wins);
            Assert.Equal(3, x.Points);
        });
        // All three have identical W/L, GD=0, GF=3 → name is the tiebreaker.
        Assert.Equal(new[] { "Comets", "Eagles", "Hawks" }, s.Select(x => x.TeamName).ToArray());
    }
}
