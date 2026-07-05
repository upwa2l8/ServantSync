using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// One row of the standings table. Mutable so the calculator can fill it
/// in-place; consumers should treat it as read-only once returned.
/// </summary>
public record TeamStanding
{
    public int TeamId { get; init; }
    public string TeamName { get; init; } = "";
    public TeamAgeBracket AgeBracket { get; init; }
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference { get; set; }
    public int Points { get; set; }
}

/// <summary>
/// Pure, deterministic standings math. Sport-agnostic: any league can pass
/// its own points-for-W/D/L scheme. Only games that
/// <see cref="Game.CountsForStandings"/> (= Status == Played with both
/// scores set, start time in the past) are counted.
///
/// Tie-breaking order:
///   1. Points (desc)
///   2. Goal difference (desc)
///   3. Goals scored (desc)
///   4. Team name (asc, deterministic)
/// </summary>
public static class StandingsCalculator
{
    public static List<TeamStanding> Calculate(
        IEnumerable<Game> games,
        IEnumerable<Team> teams,
        int pointsForWin = 3,
        int pointsForDraw = 1,
        int pointsForLoss = 0,
        DateTime? asOfUtc = null)
    {
        asOfUtc ??= DateTime.UtcNow;
        var standings = teams.ToDictionary(
            t => t.Id,
            t => new TeamStanding
            {
                TeamId = t.Id,
                TeamName = t.Name,
                AgeBracket = t.AgeBracket,
            });

        foreach (var g in games)
        {
            if (!g.CountsForStandings(asOfUtc.Value)) continue;
            if (!standings.TryGetValue(g.HomeTeamId, out var home)) continue;
            if (!standings.TryGetValue(g.AwayTeamId, out var away)) continue;

            var hs = g.HomeScore!.Value;
            var aws = g.AwayScore!.Value;

            home.Played++;
            away.Played++;
            home.GoalsFor += hs;
            home.GoalsAgainst += aws;
            away.GoalsFor += aws;
            away.GoalsAgainst += hs;

            if (hs > aws)
            {
                home.Wins++;
                home.Points += pointsForWin;
                away.Losses++;
                away.Points += pointsForLoss;
            }
            else if (hs < aws)
            {
                away.Wins++;
                away.Points += pointsForWin;
                home.Losses++;
                home.Points += pointsForLoss;
            }
            else
            {
                home.Draws++;
                home.Points += pointsForDraw;
                away.Draws++;
                away.Points += pointsForDraw;
            }
        }

        foreach (var s in standings.Values)
        {
            s.GoalDifference = s.GoalsFor - s.GoalsAgainst;
        }

        return standings.Values
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.GoalDifference)
            .ThenByDescending(s => s.GoalsFor)
            .ThenBy(s => s.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
