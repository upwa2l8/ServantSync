using Microsoft.EntityFrameworkCore;
using ServantSync.Data;

namespace ServantSync.Services;

public interface IStandingsService
{
    /// <summary>
    /// Calculates standings for every team in a league ministry. The points
    /// scheme defaults to 3-1-0 (soccer convention) but can be overridden
    /// per-call for sports with different scoring (e.g. 2-1-0 for basketball).
    /// </summary>
    Task<List<TeamStanding>> CalculateForMinistryAsync(
        int ministryId,
        int? pointsForWin = null,
        int? pointsForDraw = null,
        int? pointsForLoss = null,
        CancellationToken ct = default);
}

public class StandingsService : IStandingsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public StandingsService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<TeamStanding>> CalculateForMinistryAsync(
        int ministryId,
        int? pointsForWin = null,
        int? pointsForDraw = null,
        int? pointsForLoss = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var teams = await db.Teams
            .Where(t => t.MinistryId == ministryId)
            .AsNoTracking()
            .ToListAsync(ct);
        var games = await db.Games
            .Where(g => g.MinistryId == ministryId)
            .AsNoTracking()
            .ToListAsync(ct);

        return StandingsCalculator.Calculate(
            games,
            teams,
            pointsForWin ?? 3,
            pointsForDraw ?? 1,
            pointsForLoss ?? 0);
    }
}
