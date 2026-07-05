using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public interface IGameService
{
    Task<GameValidationResult> ScheduleGameAsync(int ministryId, int homeTeamId, int awayTeamId, int arenaId, DateTime startUtc, DateTime endUtc, string? notes = null, CancellationToken ct = default);
    Task<GameValidationResult> UpdateGameAsync(int gameId, int homeTeamId, int awayTeamId, int arenaId, DateTime startUtc, DateTime endUtc, string? notes = null, CancellationToken ct = default);
    Task<Game> SetScoreAsync(int gameId, int homeScore, int awayScore, CancellationToken ct = default);
    Task<Game> SetStatusAsync(int gameId, GameStatus status, CancellationToken ct = default);
    Task DeleteGameAsync(int gameId, CancellationToken ct = default);
    Task<Game?> GetGameAsync(int gameId, CancellationToken ct = default);
    Task<List<Game>> ListForMinistryAsync(int ministryId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken ct = default);
    Task<List<Game>> ListForArenaAsync(int arenaId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

public record GameValidationResult(
    bool Succeeded,
    Game? Game,
    IReadOnlyList<string> Errors)
{
    public static GameValidationResult Ok(Game g) =>
        new(true, g, Array.Empty<string>());

    public static GameValidationResult Fail(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

public class GameService : IGameService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public GameService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<GameValidationResult> ScheduleGameAsync(int ministryId, int homeTeamId, int awayTeamId, int arenaId, DateTime startUtc, DateTime endUtc, string? notes = null, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (endUtc <= startUtc) errors.Add("End time must be after start time.");
        if (homeTeamId == awayTeamId) errors.Add("Home and away teams must be different.");
        if (errors.Count > 0) return GameValidationResult.Fail(errors);

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Both teams must belong to the same ministry (league).
        var teamMinistries = await db.Teams
            .Where(t => t.Id == homeTeamId || t.Id == awayTeamId)
            .Select(t => new { t.Id, t.MinistryId })
            .ToListAsync(ct);
        if (teamMinistries.Count != 2
            || teamMinistries.Any(t => t.MinistryId != ministryId))
        {
            errors.Add("Both teams must belong to the same league as this game.");
        }

        // Arena must belong to the same organization as the ministry.
        var ministryOrgId = await db.Ministries
            .Where(m => m.Id == ministryId)
            .Select(m => m.OrganizationId)
            .FirstOrDefaultAsync(ct);
        var arenaOrgId = await db.Arenas
            .Where(a => a.Id == arenaId)
            .Select(a => a.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (ministryOrgId == 0 || arenaOrgId == 0 || ministryOrgId != arenaOrgId)
        {
            errors.Add("Arena must belong to the same organization as the league.");
        }

        // Arena conflict: no other non-terminal game at the same arena overlapping in time.
        // (No need to exclude the current game by id — this method is for new games
        // only; UpdateGameAsync has its own exclusion-by-id path.)
        if (errors.Count == 0)
        {
            var conflicts = await db.Games
                .Where(g => g.ArenaId == arenaId
                    && g.Status != GameStatus.Cancelled
                    && g.Status != GameStatus.Postponed
                    && g.StartUtc < endUtc
                    && g.EndUtc > startUtc)
                .Select(g => new
                {
                    g.Id,
                    g.HomeTeamId,
                    g.AwayTeamId,
                    g.StartUtc,
                    g.EndUtc,
                    g.Status,
                    HomeName = g.HomeTeam.Name,
                    AwayName = g.AwayTeam.Name,
                })
                .AsNoTracking()
                .ToListAsync(ct);
            if (conflicts.Count > 0)
            {
                var c = conflicts[0];
                errors.Add($"Arena is already booked from {c.StartUtc:u} to {c.EndUtc:u} ({c.HomeName} vs {c.AwayName}).");
            }
        }

        if (errors.Count > 0) return GameValidationResult.Fail(errors);

        var game = new Game
        {
            MinistryId = ministryId,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            ArenaId = arenaId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = GameStatus.Scheduled,
            Notes = notes,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
        return GameValidationResult.Ok(game);
    }

    public async Task<GameValidationResult> UpdateGameAsync(int gameId, int homeTeamId, int awayTeamId, int arenaId, DateTime startUtc, DateTime endUtc, string? notes = null, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (endUtc <= startUtc) errors.Add("End time must be after start time.");
        if (homeTeamId == awayTeamId) errors.Add("Home and away teams must be different.");
        if (errors.Count > 0) return GameValidationResult.Fail(errors);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Games.FindAsync(new object?[] { gameId }, ct);
        if (existing is null) return GameValidationResult.Fail(new[] { "Game not found." });

        var conflict = await db.Games
            .Where(g => g.Id != gameId
                && g.ArenaId == arenaId
                && g.Status != GameStatus.Cancelled
                && g.Status != GameStatus.Postponed
                && g.StartUtc < endUtc
                && g.EndUtc > startUtc)
            .Select(g => new
            {
                g.Id,
                g.StartUtc,
                g.EndUtc,
                HomeName = g.HomeTeam.Name,
                AwayName = g.AwayTeam.Name,
            })
            .FirstOrDefaultAsync(ct);
        if (conflict is not null)
        {
            errors.Add($"Arena is already booked from {conflict.StartUtc:u} to {conflict.EndUtc:u} ({conflict.HomeName} vs {conflict.AwayName}).");
        }
        if (errors.Count > 0) return GameValidationResult.Fail(errors);

        existing.HomeTeamId = homeTeamId;
        existing.AwayTeamId = awayTeamId;
        existing.ArenaId = arenaId;
        existing.StartUtc = startUtc;
        existing.EndUtc = endUtc;
        existing.Notes = notes;
        await db.SaveChangesAsync(ct);
        return GameValidationResult.Ok(existing);
    }

    public async Task<Game> SetScoreAsync(int gameId, int homeScore, int awayScore, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var game = await db.Games.FindAsync(new object?[] { gameId }, ct)
            ?? throw new InvalidOperationException("Game not found.");
        game.HomeScore = homeScore;
        game.AwayScore = awayScore;
        game.Status = GameStatus.Played;
        await db.SaveChangesAsync(ct);
        return game;
    }

    public async Task<Game> SetStatusAsync(int gameId, GameStatus status, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var game = await db.Games.FindAsync(new object?[] { gameId }, ct)
            ?? throw new InvalidOperationException("Game not found.");
        game.Status = status;
        await db.SaveChangesAsync(ct);
        return game;
    }

    public async Task DeleteGameAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var game = await db.Games.FindAsync(new object?[] { gameId }, ct);
        if (game is null) return;
        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Game?> GetGameAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Games
            .Include(g => g.Ministry)
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Include(g => g.Arena)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);
    }

    public async Task<List<Game>> ListForMinistryAsync(int ministryId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Include(g => g.Arena)
            .Where(g => g.MinistryId == ministryId);
        if (fromUtc.HasValue) query = query.Where(g => g.StartUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(g => g.StartUtc < toUtc.Value);
        return await query.OrderBy(g => g.StartUtc).AsNoTracking().ToListAsync(ct);
    }

    public async Task<List<Game>> ListForArenaAsync(int arenaId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.ArenaId == arenaId && g.StartUtc < toUtc && g.EndUtc > fromUtc)
            .OrderBy(g => g.StartUtc)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
