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

    /// <summary>
    /// Round-AY read helper: list the players currently on this team's
    /// roster with their primary-contact (parent/guardian) info eager-
    /// loaded so the page can render rows in a single round-trip. Mirrors
    /// the "active player" convention used by <c>Teams/Detail.razor</c>:
    /// a player is active iff <c>Player.LeftUtc</c> is null OR
    /// <c>LeftUtc &gt; DateTime.UtcNow</c>, exactly the same rule the
    /// roster table on the team detail page applies. Coaches / managers
    /// use this to call/email every parent before a game without
    /// searching People one-by-one.
    /// <para>
    /// Sorted by JerseyNumber ascending (players without a jersey sort
    /// LAST so the "real" roster comes first), then LastName, then
    /// FirstName — stable UI order that matches both the existing
    /// Detail.razor roster table and the Signups page.
    /// </para>
    /// <para>
    /// Caller gating is the page's responsibility
    /// (<c>OrgAuthService.CanManageTeamAsync</c>); this method is a
    /// pure read query. Empty/invalid team id returns an empty list
    /// rather than throwing.
    /// </para>
    /// </summary>
    Task<List<Player>> ListActivePlayersWithContactsAsync(int teamId, CancellationToken ct = default);
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

    public async Task<List<Player>> ListActivePlayersWithContactsAsync(int teamId, CancellationToken ct = default)
    {
        if (teamId <= 0) return new();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        // "Active" convention matches Teams/Detail.razor exactly so the
        // page and this query stay in lock-step: a player is on the
        // current roster if they have never left (LeftUtc null) OR they
        // have a scheduled future transition (LeftUtc > now). Players
        // who left in the past are excluded from the contact surface
        // because their primary-contact information may be stale.
        // Eager-load the primary-contact Person so the page can render
        // the parent's display name without a per-row round-trip;
        // PrimaryContactPerson is nullable (players without a logged-in
        // contact skip this join row but still appear in the result with
        // their denormalized phone/email columns).
        return await db.Players
            .Include(p => p.PrimaryContactPerson)
            .Where(p => p.TeamId == teamId && (p.LeftUtc == null || p.LeftUtc > now))
            // NJLS LAST: in LINQ-to-Objects AND EF Core SQL alike,
            // `bool` ascending orders false(0) < true(1). Sorting
            // ASC by HasValue would put jersey-NULL players FIRST
            // (false before true). OrderByDescending flips that so
            // true(1) (has a jersey) sorts before false(0) (null
            // jersey), placing the no-jersey rows at the end of the
            // roster — which matches what Teams/Detail.razor's roster
            // table does on the same data.
            .OrderByDescending(p => p.JerseyNumber.HasValue)
            .ThenBy(p => p.JerseyNumber)
            .ThenBy(p => p.LastName).ThenBy(p => p.FirstName)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
