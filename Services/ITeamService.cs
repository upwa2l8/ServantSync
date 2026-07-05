using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public interface ITeamService
{
    Task<Team> CreateTeamAsync(int ministryId, string name, TeamAgeBracket ageBracket, string? gender = null, string? description = null, string? coachPersonUserId = null, CancellationToken ct = default);
    Task<Team> UpdateTeamAsync(int teamId, string name, TeamAgeBracket ageBracket, string? gender = null, string? description = null, string? coachPersonUserId = null, bool isActive = true, CancellationToken ct = default);
    Task DeleteTeamAsync(int teamId, CancellationToken ct = default);
    Task<Team?> GetTeamAsync(int teamId, CancellationToken ct = default);
    Task<List<Team>> ListForMinistryAsync(int ministryId, CancellationToken ct = default);
    Task<Player> AddPlayerAsync(int teamId, string firstName, string lastName, DateTime? dateOfBirth, int? jerseyNumber, string? position, string? primaryContactPersonUserId, string? primaryContactPhone, string? primaryContactEmail, string? notes, CancellationToken ct = default);
    Task<Player> UpdatePlayerAsync(int playerId, string firstName, string lastName, DateTime? dateOfBirth, int? jerseyNumber, string? position, string? primaryContactPersonUserId, string? primaryContactPhone, string? primaryContactEmail, string? notes, CancellationToken ct = default);
    Task RemovePlayerAsync(int playerId, CancellationToken ct = default);
}

public class TeamService : ITeamService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TeamService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<Team> CreateTeamAsync(int ministryId, string name, TeamAgeBracket ageBracket, string? gender = null, string? description = null, string? coachPersonUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        await using var db = await _factory.CreateDbContextAsync(ct);
        var exists = await db.Teams.AnyAsync(t => t.MinistryId == ministryId && t.Name == name, ct);
        if (exists) throw new InvalidOperationException($"A team named '{name}' already exists in this league.");
        var team = new Team
        {
            MinistryId = ministryId,
            Name = name,
            AgeBracket = ageBracket,
            Gender = gender,
            Description = description,
            CoachPersonUserId = coachPersonUserId,
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return team;
    }

    public async Task<Team> UpdateTeamAsync(int teamId, string name, TeamAgeBracket ageBracket, string? gender = null, string? description = null, string? coachPersonUserId = null, bool isActive = true, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var team = await db.Teams.FindAsync(new object?[] { teamId }, ct)
            ?? throw new InvalidOperationException("Team not found.");
        team.Name = name;
        team.AgeBracket = ageBracket;
        team.Gender = gender;
        team.Description = description;
        team.CoachPersonUserId = coachPersonUserId;
        await db.SaveChangesAsync(ct);
        return team;
    }

    public async Task DeleteTeamAsync(int teamId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var team = await db.Teams.FindAsync(new object?[] { teamId }, ct);
        if (team is null) return;
        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Team?> GetTeamAsync(int teamId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Teams
            .Include(t => t.Ministry)
            .Include(t => t.CoachPerson)
            .Include(t => t.Players)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == teamId, ct);
    }

    public async Task<List<Team>> ListForMinistryAsync(int ministryId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Teams
            .Include(t => t.CoachPerson)
            .Where(t => t.MinistryId == ministryId)
            .OrderBy(t => t.AgeBracket).ThenBy(t => t.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<Player> AddPlayerAsync(int teamId, string firstName, string lastName, DateTime? dateOfBirth, int? jerseyNumber, string? position, string? primaryContactPersonUserId, string? primaryContactPhone, string? primaryContactEmail, string? notes, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Last name is required.", nameof(lastName));
        await using var db = await _factory.CreateDbContextAsync(ct);
        var player = new Player
        {
            TeamId = teamId,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            JerseyNumber = jerseyNumber,
            Position = position,
            PrimaryContactPersonUserId = primaryContactPersonUserId,
            PrimaryContactPhone = primaryContactPhone,
            PrimaryContactEmail = primaryContactEmail,
            Notes = notes,
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);
        return player;
    }

    public async Task<Player> UpdatePlayerAsync(int playerId, string firstName, string lastName, DateTime? dateOfBirth, int? jerseyNumber, string? position, string? primaryContactPersonUserId, string? primaryContactPhone, string? primaryContactEmail, string? notes, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var player = await db.Players.FindAsync(new object?[] { playerId }, ct)
            ?? throw new InvalidOperationException("Player not found.");
        player.FirstName = firstName;
        player.LastName = lastName;
        player.DateOfBirth = dateOfBirth;
        player.JerseyNumber = jerseyNumber;
        player.Position = position;
        player.PrimaryContactPersonUserId = primaryContactPersonUserId;
        player.PrimaryContactPhone = primaryContactPhone;
        player.PrimaryContactEmail = primaryContactEmail;
        player.Notes = notes;
        await db.SaveChangesAsync(ct);
        return player;
    }

    public async Task RemovePlayerAsync(int playerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var player = await db.Players.FindAsync(new object?[] { playerId }, ct);
        if (player is null) return;
        // Soft-delete by setting LeftUtc rather than hard-deleting, so that
        // historical Games (which don't reference Players) still have a
        // stable team, and so a player can be re-added later without a
        // duplicate (the (jersey-number) constraint would otherwise fire).
        player.LeftUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
