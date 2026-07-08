using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;

namespace ServantSync.Data;

/// <summary>
/// Idempotent bootstrap seeder. Runs once when the Organizations table is
/// empty, creating a demo organization (Demo Church) with two ministries
/// (Worship, Children's), the Springfield Youth Soccer League ministry
/// with three sub-ministries (Referees, Concessions, Devotion), three
/// playing arenas, four teams, sixteen players, six games, a Concussion
/// training requirement for game-day referees, and the matching
/// volunteer-shift assignments.
///
/// To force a re-seed: delete the SQLite file (default <c>servantsync.db</c>
/// at the repo root) and restart.
/// </summary>
public class DatabaseSeeder
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IUploadPathProvider _uploadPaths;
    private readonly ILogger<DatabaseSeeder> _log;

    public DatabaseSeeder(
        IDbContextFactory<ApplicationDbContext> factory,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IUploadPathProvider uploadPaths,
        ILogger<DatabaseSeeder> log)
    {
        _factory = factory;
        _userManager = userManager;
        _roleManager = roleManager;
        _uploadPaths = uploadPaths;
        _log = log;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // ---- Identity users (self-healing pass) ----
        // Runs on every startup regardless of whether the rest of the seed
        // data is present, so a corrupted user (e.g. one written without a
        // PasswordHash by an older bug) gets fixed without requiring a DB
        // wipe. Idempotent — if the user already has a password, this is a
        // no-op.
        var adminUser        = await EnsureUserAsync("admin@demo.local",        "Passw0rd!", ct);
        var coordinatorUser  = await EnsureUserAsync("coordinator@demo.local",  "Passw0rd!", ct);
        var volunteerUser    = await EnsureUserAsync("volunteer@demo.local",    "Passw0rd!", ct);
        var volunteer2User   = await EnsureUserAsync("volunteer2@demo.local",   "Passw0rd!", ct);
        var leagueAdminUser  = await EnsureUserAsync("league-admin@demo.local", "Passw0rd!", ct);
        var coach1User       = await EnsureUserAsync("coach1@demo.local",       "Passw0rd!", ct);
        var coach2User       = await EnsureUserAsync("coach2@demo.local",       "Passw0rd!", ct);
        var ref1User         = await EnsureUserAsync("ref1@demo.local",         "Passw0rd!", ct);
        var ref2User         = await EnsureUserAsync("ref2@demo.local",         "Passw0rd!", ct);
        var devotionUser     = await EnsureUserAsync("devotion@demo.local",     "Passw0rd!", ct);
        var concessionUser   = await EnsureUserAsync("concession@demo.local",   "Passw0rd!", ct);
        var parent1User      = await EnsureUserAsync("parent1@demo.local",      "Passw0rd!", ct);
        var parent2User      = await EnsureUserAsync("parent2@demo.local",      "Passw0rd!", ct);

        await using var db = await _factory.CreateDbContextAsync(ct);

        // ---- SystemAdmin role + bootstrap promotion ----
        // Runs on EVERY startup (not gated on the Organizations-empty
        // early-return below) so a deployment that adds an email to
        // SERVANTSYNC_BOOTSTRAP_SYSTEMADMIN_EMAILS picks it up on
        // restart without a DB wipe. Promotion is monotonic: we never
        // remove anyone from the role here. The role-name literal
        // "SystemAdmin" matches OrgAuthService.IsSystemAdminAsync's
        // NormalizedName lookup so the two are wired against a single
        // source-of-truth string.
        await EnsureSystemAdminRoleAsync(ct);
        await PromoteSystemAdminEmailsAsync(ct);

        if (await db.Organizations.AnyAsync(ct))
        {
            _log.LogInformation("DatabaseSeeder: skipping data seed — Organizations already has data.");
            return;
        }

        _log.LogInformation("DatabaseSeeder: empty database detected, seeding sample data.");

        // SqlServerRetryingExecutionStrategy does not support user-initiated
        // transactions. Wrap the entire seed in the execution strategy so
        // retries can replay the transaction block atomically.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ---- Persons (one row per IdentityUser, PK = UserId) ----
        var persons = new[]
        {
            new Person { UserId = adminUser.Id,       FirstName = "Alex",   LastName = "Admin" },
            new Person { UserId = coordinatorUser.Id, FirstName = "Chris",  LastName = "Coordinator" },
            new Person { UserId = volunteerUser.Id,   FirstName = "Vee",    LastName = "Volunteer" },
            new Person { UserId = volunteer2User.Id,  FirstName = "Val",    LastName = "Walker" },
            new Person { UserId = leagueAdminUser.Id, FirstName = "Logan",  LastName = "League-Admin" },
            new Person { UserId = coach1User.Id,      FirstName = "Casey",  LastName = "Coach" },
            new Person { UserId = coach2User.Id,      FirstName = "Morgan", LastName = "Coach" },
            new Person { UserId = ref1User.Id,        FirstName = "Riley",  LastName = "Referee" },
            new Person { UserId = ref2User.Id,        FirstName = "Quinn",  LastName = "Referee" },
            new Person { UserId = devotionUser.Id,    FirstName = "Drew",   LastName = "Devotion" },
            new Person { UserId = concessionUser.Id,  FirstName = "Sam",    LastName = "Concessions" },
            new Person { UserId = parent1User.Id,     FirstName = "Pat",    LastName = "Parent" },
            new Person { UserId = parent2User.Id,     FirstName = "Jordan", LastName = "Parent" },
        };
        db.People.AddRange(persons);

        // ---- Organization + memberships (every user joins Demo Church) ----
        var org = new Organization
        {
            Name = "Demo Church",
            Description = "Sample organization created on first run.",
            Address = "123 Main St, Springfield",
            ContactEmail = "office@demo.local",
            ContactPhone = "555-0100",
            // Seed a registration token so the demo's "Share register link"
            // affordance has something to display on first launch. Admins
            // can rotate this from the org's Detail page.
            RegistrationToken = Guid.NewGuid().ToString("N"),
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);

        db.OrganizationMemberships.AddRange(
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = adminUser.Id,       Role = OrganizationRole.Admin },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = coordinatorUser.Id, Role = OrganizationRole.Coordinator },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = volunteerUser.Id,   Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = volunteer2User.Id,  Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = leagueAdminUser.Id, Role = OrganizationRole.Admin },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = coach1User.Id,      Role = OrganizationRole.Coordinator },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = coach2User.Id,      Role = OrganizationRole.Coordinator },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = ref1User.Id,        Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = ref2User.Id,        Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = devotionUser.Id,    Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = concessionUser.Id,  Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = parent1User.Id,     Role = OrganizationRole.Volunteer },
            new OrganizationMembership { OrganizationId = org.Id, PersonUserId = parent2User.Id,     Role = OrganizationRole.Volunteer });

        // ---- Church ministries ----
        var worship = new Ministry
        {
            OrganizationId = org.Id,
            Name = "Worship Team",
            Description = "Sets the tone for Sunday services.",
            CoordinatorEmail = "worship@demo.local",
        };
        var kids = new Ministry
        {
            OrganizationId = org.Id,
            Name = "Children's Ministry",
            Description = "Sunday school and nursery care.",
            CoordinatorEmail = "kids@demo.local",
        };
        db.Ministries.AddRange(worship, kids);
        await db.SaveChangesAsync(ct);

        // ---- Sports league ministry (parent) + 3 sub-ministries ----
        var soccer = new Ministry
        {
            OrganizationId = org.Id,
            Name = "Springfield Youth Soccer League",
            Description = "Recreational youth soccer for U10 and U12 divisions.",
            CoordinatorPersonUserId = leagueAdminUser.Id,
            CoordinatorEmail = "league@demo.local",
        };
        db.Ministries.Add(soccer);
        await db.SaveChangesAsync(ct);

        var referees = new Ministry
        {
            OrganizationId = org.Id,
            Name = "Referees",
            Description = "Game-day officiating crew. Coordinator manages the referee schedule.",
            CoordinatorPersonUserId = ref1User.Id,
            ParentMinistryId = soccer.Id,
        };
        var concessions = new Ministry
        {
            OrganizationId = org.Id,
            Name = "Concessions",
            Description = "Concession stand volunteers for game day.",
            CoordinatorPersonUserId = concessionUser.Id,
            ParentMinistryId = soccer.Id,
        };
        var devotion = new Ministry
        {
            OrganizationId = org.Id,
            Name = "Devotion",
            Description = "Pre-game devotion leaders for each game.",
            CoordinatorPersonUserId = devotionUser.Id,
            ParentMinistryId = soccer.Id,
        };
        db.Ministries.AddRange(referees, concessions, devotion);
        await db.SaveChangesAsync(ct);

        // ---- Service slots (church) ----
        var sound = new ServiceSlot
        {
            MinistryId = worship.Id,
            Name = "Sunday Sound Tech",
            Description = "Run the mixing board during the 10:30 service.",
            Location = "Sound booth",
            DefaultDurationMinutes = 120,
        };
        var vocals = new ServiceSlot
        {
            MinistryId = worship.Id,
            Name = "Sunday Vocals",
            Description = "Lead worship vocals.",
            Location = "Stage",
            DefaultDurationMinutes = 90,
        };
        var sundaySchool = new ServiceSlot
        {
            MinistryId = kids.Id,
            Name = "Sunday School Helper",
            Description = "Lead a small group of K\u20132 graders.",
            Location = "Room 204",
            DefaultDurationMinutes = 75,
        };
        db.ServiceSlots.AddRange(sound, vocals, sundaySchool);
        await db.SaveChangesAsync(ct);

        // ---- Service slots (soccer league + sub-ministries) ----
        var u10BoysHead   = new ServiceSlot { MinistryId = soccer.Id,     Name = "U10 Boys Head Coach",    Description = "Head coach for the U10 Boys team.", DefaultDurationMinutes = 90 };
        var u10BoysAsst   = new ServiceSlot { MinistryId = soccer.Id,     Name = "U10 Boys Asst Coach",    Description = "Assistant coach for the U10 Boys team." };
        var u12GirlsHead  = new ServiceSlot { MinistryId = soccer.Id,     Name = "U12 Girls Head Coach",   Description = "Head coach for the U12 Girls team.", DefaultDurationMinutes = 90 };
        var u12GirlsAsst  = new ServiceSlot { MinistryId = soccer.Id,     Name = "U12 Girls Asst Coach",   Description = "Assistant coach for the U12 Girls team." };
        var refSlot       = new ServiceSlot { MinistryId = referees.Id,  Name = "Game-Day Referee",       Description = "Officiate a Saturday game (Concussion training required).", DefaultDurationMinutes = 120, Location = "Assigned arena" };
        var concessionSlot= new ServiceSlot { MinistryId = concessions.Id, Name = "Concession Stand Worker", Description = "Run the concession stand on game day.", DefaultDurationMinutes = 240, Location = "Concession stand" };
        var devotionSlot  = new ServiceSlot { MinistryId = devotion.Id,  Name = "Devotion Leader",        Description = "Lead the 5-minute pre-game devotion.", DefaultDurationMinutes = 15, Location = "Team bench" };
        db.ServiceSlots.AddRange(u10BoysHead, u10BoysAsst, u12GirlsHead, u12GirlsAsst, refSlot, concessionSlot, devotionSlot);
        await db.SaveChangesAsync(ct);

        // ---- Arenas (org-scoped, shared by all leagues) ----
        var field1 = new Arena { OrganizationId = org.Id, Name = "Field 1", SurfaceType = "Grass" };
        var field2 = new Arena { OrganizationId = org.Id, Name = "Field 2", SurfaceType = "Grass" };
        var gymA   = new Arena { OrganizationId = org.Id, Name = "Gym A",   SurfaceType = "Hardwood", Capacity = 300 };
        db.Arenas.AddRange(field1, field2, gymA);
        await db.SaveChangesAsync(ct);

        // ---- Training content (per-org since round N) ----
        // Both seeded items belong to the Demo Church org. There is no
        // cross-org sharing — a TrainingContent's owner is one Organization
        // and the FK is NOT NULL with a CHECK that prevents reuse. Mirrors
        // the schema's expectation so the seed never trips the migration's
        // backfill phase.
        var safeSpaces = new TrainingContent
        {
            OrganizationId = org.Id,
            Title = "Safe Spaces & Child Protection",
            Description = "Annual child-protection training required for everyone working with minors.",
            Format = TrainingFormat.Video,
            FilePathOrUrl = "https://www.youtube.com/embed/dQw4w9WgXcQ",
            EstimatedDuration = TimeSpan.FromMinutes(45),
            VersionLabel = "2026 Edition",
        };
        var concussion = new TrainingContent
        {
            OrganizationId = org.Id,
            Title = "Concussion & Head Injury Awareness",
            Description = "Required annually for all game-day referees.",
            Format = TrainingFormat.Video,
            FilePathOrUrl = "https://www.youtube.com/embed/dQw4w9WgXcQ",
            EstimatedDuration = TimeSpan.FromMinutes(20),
            VersionLabel = "2026 Edition",
        };
        db.TrainingContents.AddRange(safeSpaces, concussion);
        await db.SaveChangesAsync(ct);

        // ---- Training requirements ----
        db.TrainingRequirements.Add(new TrainingRequirement
        {
            OrganizationId = org.Id,
            TrainingContentId = safeSpaces.Id,
            Cadence = TrainingCadence.Yearly,
        });
        db.TrainingRequirements.Add(new TrainingRequirement
        {
            ServiceSlotId = sundaySchool.Id,
            TrainingContentId = safeSpaces.Id,
            Cadence = TrainingCadence.Yearly,
        });
        db.TrainingRequirements.Add(new TrainingRequirement
        {
            ServiceSlotId = refSlot.Id,
            TrainingContentId = concussion.Id,
            Cadence = TrainingCadence.Yearly,
        });

        // ---- Training completions ----
        // Vee & Val: Safe-Spaces-compliant (1-2 months ago). Ref1 & Ref2: Concussion-compliant (1-2 months ago).
        // All completions are valid for ~10+ months under the Yearly cadence.
        db.TrainingCompletions.AddRange(
            new TrainingCompletion
            {
                PersonUserId = volunteerUser.Id, TrainingContentId = safeSpaces.Id, TrainingContentVersion = safeSpaces.Version,
                CompletionUtc = DateTime.UtcNow.AddMonths(-1), ExpiresUtc = DateTime.UtcNow.AddMonths(11),
            },
            new TrainingCompletion
            {
                PersonUserId = volunteer2User.Id, TrainingContentId = safeSpaces.Id, TrainingContentVersion = safeSpaces.Version,
                CompletionUtc = DateTime.UtcNow.AddMonths(-2), ExpiresUtc = DateTime.UtcNow.AddMonths(10),
            },
            new TrainingCompletion
            {
                PersonUserId = ref1User.Id, TrainingContentId = concussion.Id, TrainingContentVersion = concussion.Version,
                CompletionUtc = DateTime.UtcNow.AddMonths(-2), ExpiresUtc = DateTime.UtcNow.AddMonths(10),
            },
            new TrainingCompletion
            {
                PersonUserId = ref2User.Id, TrainingContentId = concussion.Id, TrainingContentVersion = concussion.Version,
                CompletionUtc = DateTime.UtcNow.AddMonths(-1), ExpiresUtc = DateTime.UtcNow.AddMonths(11),
            });

        // ---- Sample church assignments (this Sunday + next Sunday in UTC) ----
        var (thisSunday, nextSunday) = ResolveSundayWindow();
        db.Assignments.AddRange(
            new Assignment { PersonUserId = volunteerUser.Id, ServiceSlotId = sound.Id, StartUtc = thisSunday, EndUtc = thisSunday.AddMinutes(sound.DefaultDurationMinutes ?? 120), Status = AssignmentStatus.Scheduled },
            new Assignment { PersonUserId = volunteer2User.Id, ServiceSlotId = vocals.Id, StartUtc = thisSunday, EndUtc = thisSunday.AddMinutes(vocals.DefaultDurationMinutes ?? 90), Status = AssignmentStatus.Scheduled },
            new Assignment { PersonUserId = volunteerUser.Id, ServiceSlotId = sundaySchool.Id, StartUtc = thisSunday, EndUtc = thisSunday.AddMinutes(sundaySchool.DefaultDurationMinutes ?? 75), Status = AssignmentStatus.Scheduled },
            new Assignment { PersonUserId = volunteer2User.Id, ServiceSlotId = sound.Id, StartUtc = nextSunday, EndUtc = nextSunday.AddMinutes(sound.DefaultDurationMinutes ?? 120), Status = AssignmentStatus.Scheduled },
            new Assignment { PersonUserId = volunteerUser.Id, ServiceSlotId = vocals.Id, StartUtc = nextSunday, EndUtc = nextSunday.AddMinutes(vocals.DefaultDurationMinutes ?? 90), Status = AssignmentStatus.Scheduled },
            new Assignment { PersonUserId = volunteerUser.Id, ServiceSlotId = sound.Id, StartUtc = nextSunday.AddMinutes(30), EndUtc = nextSunday.AddMinutes(30).AddMinutes(sound.DefaultDurationMinutes ?? 120), Status = AssignmentStatus.Scheduled });

        // ---- Teams ----
        var eagles = new Team { MinistryId = soccer.Id, Name = "Eagles",  AgeBracket = TeamAgeBracket.U10, Gender = "Boys",  CoachPersonUserId = coach1User.Id, Description = "U10 boys travel team." };
        var hawks  = new Team { MinistryId = soccer.Id, Name = "Hawks",   AgeBracket = TeamAgeBracket.U10, Gender = "Boys",  CoachPersonUserId = coach1User.Id, Description = "U10 boys rec team." };
        var comets = new Team { MinistryId = soccer.Id, Name = "Comets",  AgeBracket = TeamAgeBracket.U12, Gender = "Girls", CoachPersonUserId = coach2User.Id, Description = "U12 girls travel team." };
        var stars  = new Team { MinistryId = soccer.Id, Name = "Stars",   AgeBracket = TeamAgeBracket.U12, Gender = "Girls", CoachPersonUserId = coach2User.Id, Description = "U12 girls rec team." };
        db.Teams.AddRange(eagles, hawks, comets, stars);
        await db.SaveChangesAsync(ct);

        // ---- Players (16: 4 per team, parents are demo Person records) ----
        var playerDobs = new Dictionary<string, DateTime>
        {
            ["Ethan"]  = new(2016,  5, 12), ["Evan"]   = new(2016,  9,  3), ["Elliot"] = new(2016,  3, 18), ["Erin"]    = new(2016,  7, 22),
            ["Hank"]   = new(2016,  2,  9), ["Hazel"]  = new(2016, 11, 14), ["Henry"]  = new(2016,  4, 28), ["Halle"]   = new(2016,  8, 17),
            ["Camila"] = new(2014,  6, 11), ["Carlos"] = new(2014, 12,  4), ["Camilo"] = new(2014,  9, 25), ["Catalina"]= new(2014,  5, 30),
            ["Sage"]   = new(2014,  3,  8), ["Selena"] = new(2014, 10, 19), ["Sergio"] = new(2014,  7,  6), ["Sienna"]  = new(2014, 11, 27),
        };
        var players = new List<Player>();
        void AddPlayer(Team t, string first, int jersey, string position, string parentUserId, string parentEmail)
        {
            players.Add(new Player
            {
                TeamId = t.Id, FirstName = first, LastName = t.Name, DateOfBirth = playerDobs[first],
                JerseyNumber = jersey, Position = position,
                PrimaryContactPersonUserId = parentUserId,
                PrimaryContactPhone = parentEmail == parent1User.Email! ? "555-0101" : "555-0102",
                PrimaryContactEmail = parentEmail,
            });
        }
        // Eagles (Coach Casey)
        AddPlayer(eagles, "Ethan",  7, "Forward",     parent1User.Id, parent1User.Email!);
        AddPlayer(eagles, "Evan",   4, "Midfielder",  parent1User.Id, parent1User.Email!);
        AddPlayer(eagles, "Elliot", 1, "Goalkeeper",  parent2User.Id, parent2User.Email!);
        AddPlayer(eagles, "Erin",   9, "Forward",     parent2User.Id, parent2User.Email!);
        // Hawks (Coach Casey)
        AddPlayer(hawks, "Hank",   10, "Midfielder",  parent1User.Id, parent1User.Email!);
        AddPlayer(hawks, "Hazel",   2, "Defender",    parent1User.Id, parent1User.Email!);
        AddPlayer(hawks, "Henry",   5, "Forward",     parent2User.Id, parent2User.Email!);
        AddPlayer(hawks, "Halle",  11, "Goalkeeper",  parent2User.Id, parent2User.Email!);
        // Comets (Coach Morgan)
        AddPlayer(comets, "Camila",  7, "Forward",     parent1User.Id, parent1User.Email!);
        AddPlayer(comets, "Carlos",  3, "Midfielder",  parent1User.Id, parent1User.Email!);
        AddPlayer(comets, "Camilo",  1, "Goalkeeper",  parent2User.Id, parent2User.Email!);
        AddPlayer(comets, "Catalina",10,"Defender",    parent2User.Id, parent2User.Email!);
        // Stars (Coach Morgan)
        AddPlayer(stars, "Sage",    6, "Midfielder",  parent1User.Id, parent1User.Email!);
        AddPlayer(stars, "Selena",  4, "Forward",     parent1User.Id, parent1User.Email!);
        AddPlayer(stars, "Sergio",  8, "Defender",    parent2User.Id, parent2User.Email!);
        AddPlayer(stars, "Sienna", 11, "Goalkeeper",  parent2User.Id, parent2User.Email!);
        db.Players.AddRange(players);
        await db.SaveChangesAsync(ct);

        // ---- Soccer games + volunteer shift assignments ----
        // 6 games across last Saturday (1 played), this Saturday, and next Saturday.
        // Games 5 + 6 are an intentional arena double-booking on next Saturday at
        // Field 1 to exercise GameService's arena-conflict check.
        var (lastSat, thisSat, nextSat) = ResolveSaturdayWindow();
        var game1 = new Game { MinistryId = soccer.Id, HomeTeamId = eagles.Id, AwayTeamId = hawks.Id,  ArenaId = field1.Id, StartUtc = lastSat,                 EndUtc = lastSat.AddHours(1),                  Status = GameStatus.Played, HomeScore = 3, AwayScore = 1 };
        var game2 = new Game { MinistryId = soccer.Id, HomeTeamId = comets.Id, AwayTeamId = stars.Id,  ArenaId = field2.Id, StartUtc = thisSat,                 EndUtc = thisSat.AddHours(1),                  Status = GameStatus.Scheduled };
        var game3 = new Game { MinistryId = soccer.Id, HomeTeamId = eagles.Id, AwayTeamId = comets.Id, ArenaId = field1.Id, StartUtc = thisSat.AddHours(1),     EndUtc = thisSat.AddHours(2),                  Status = GameStatus.Scheduled };
        var game4 = new Game { MinistryId = soccer.Id, HomeTeamId = hawks.Id,  AwayTeamId = stars.Id,  ArenaId = gymA.Id,   StartUtc = thisSat.AddHours(2),     EndUtc = thisSat.AddHours(3),                  Status = GameStatus.Scheduled };
        var game5 = new Game { MinistryId = soccer.Id, HomeTeamId = eagles.Id, AwayTeamId = hawks.Id,  ArenaId = field1.Id, StartUtc = nextSat,                 EndUtc = nextSat.AddHours(1),                  Status = GameStatus.Scheduled };
        var game6 = new Game { MinistryId = soccer.Id, HomeTeamId = comets.Id, AwayTeamId = stars.Id,  ArenaId = field1.Id, StartUtc = nextSat,                 EndUtc = nextSat.AddHours(1),                  Status = GameStatus.Scheduled };
        db.Games.AddRange(game1, game2, game3, game4, game5, game6);

        // Volunteer shifts: refs on games (existing Assignment pipeline).
        // Note: game5 + game6 conflict at the same arena — the coordinator would
        // normally assign different refs to each, but in the seed we leave the
        // double-book in place so the conflict path is testable.
        db.Assignments.AddRange(
            new Assignment { PersonUserId = ref1User.Id, ServiceSlotId = refSlot.Id, StartUtc = lastSat, EndUtc = lastSat.AddHours(1), Status = AssignmentStatus.Completed, Notes = "Covered last Saturday's game." },
            new Assignment { PersonUserId = ref2User.Id, ServiceSlotId = refSlot.Id, StartUtc = thisSat, EndUtc = thisSat.AddHours(3), Status = AssignmentStatus.Scheduled, Notes = "Covers this Saturday's three games." },
            new Assignment { PersonUserId = ref1User.Id, ServiceSlotId = refSlot.Id, StartUtc = nextSat, EndUtc = nextSat.AddHours(2), Status = AssignmentStatus.Scheduled, Notes = "Covers next Saturday's two games." });

        await db.SaveChangesAsync(ct);

        // ---- Sample shared documents for the Sound Tech slot ----
        // The seeder writes a couple of placeholder text files to
        // wwwroot/uploads/slots/{sound.Id}/ and seeds matching SlotDocument
        // rows so the new "Shared documents" section has something to show
        // out of the box. Coordinators can upload their own over the top.
        SeedSampleSlotDocument(sound.Id, "welcome.txt",
            "Welcome to Sound Tech!\n\nThanks for serving on the sound team. " +
            "This is a sample document seeded for the demo — delete it and upload your own. " +
            "You'll find real-world notes, mixing-board photos, and the run-of-show in the other categories.\n",
            "Welcome", "Get-started packet for new volunteers on the sound team.",
            adminUser.Id, "text/plain", db);
        SeedSampleSlotDocument(sound.Id, "checklist.txt",
            "Pre-service checklist (sample):\n  - Power on the mixing board\n  - Patch in the worship team mics\n  - Check the house speakers\n  - Pull up last week's multitrack for reference\n",
            "Setup", "Pre-service checklist for sound tech volunteers.",
            coordinatorUser.Id, "text/plain", db);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        _log.LogInformation(
            "DatabaseSeeder: complete. 13 users seeded (all Passw0rd!), 1 org, 5 ministries (3 sub-ministries of the soccer league), 3 service slots (church) + 7 service slots (soccer), 1 training content (Safe Spaces) + 1 training content (Concussion) + 3 requirements, 4 training completions, 6 church assignments (1 intentional overlap), 3 arenas, 4 teams, 16 players, 6 games (1 played, 1 intentional arena double-book on next Saturday at Field 1), 2 sample shared documents on the Sound Tech slot.");
    }

    /// <summary>
    /// Idempotent role-row provider. If the role already exists this is
    /// a single SELECT no-op; otherwise we CreateAsync with the
    /// canonical name. Naming uses Title-Case "SystemAdmin" — the
    /// NormalizedName-pattern match in OrgAuthService.IsSystemAdminAsync
    /// is case-insensitive via the standard ASP.NET Core normalization
    /// convention (Identity upper-cases on save), so the lookup key is
    /// "SYSTEMADMIN" regardless of how we name it here.
    /// </summary>
    private async Task EnsureSystemAdminRoleAsync(CancellationToken ct)
    {
        if (await _roleManager.RoleExistsAsync("SystemAdmin")) return;
        var result = await _roleManager.CreateAsync(new IdentityRole("SystemAdmin"));
        if (!result.Succeeded)
        {
            _log.LogError(
                "DatabaseSeeder: failed to create SystemAdmin role: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Promotes every email in the bootstrap set to the SystemAdmin
    /// role. The demo convenience email admin@demo.local is always
    /// included; the env var SERVANTSYNC_BOOTSTRAP_SYSTEMADMIN_EMAILS
    /// (semicolon-separated) adds production deployments without a DB
    /// edit. Unknown emails are skipped silently — a deployment whose
    /// Identity user table doesn't yet have row X for X@example.com
    /// just logs a Warning and continues so a half-bootstrapped prod
    /// doesn't 500 out.
    /// </summary>
    private async Task PromoteSystemAdminEmailsAsync(CancellationToken ct)
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin@demo.local" };
        var envAdmins = Environment.GetEnvironmentVariable("SERVANTSYNC_BOOTSTRAP_SYSTEMADMIN_EMAILS");
        if (!string.IsNullOrWhiteSpace(envAdmins))
        {
            foreach (var raw in envAdmins.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = raw.Trim();
                if (trimmed.Length > 0) emails.Add(trimmed);
            }
        }

        foreach (var email in emails)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user is null)
            {
                _log.LogWarning("DatabaseSeeder: SystemAdmin bootstrap email {Email} has no Identity row; skipping.", email);
                continue;
            }
            if (await _userManager.IsInRoleAsync(user, "SystemAdmin")) continue;
            var add = await _userManager.AddToRoleAsync(user, "SystemAdmin");
            if (!add.Succeeded)
            {
                _log.LogError(
                    "DatabaseSeeder: failed to promote {Email} to SystemAdmin: {Errors}",
                    email, string.Join("; ", add.Errors.Select(e => e.Description)));
            }
            else
            {
                _log.LogInformation("DatabaseSeeder: promoted {Email} to SystemAdmin.", email);
            }
        }
    }

    /// <summary>
    /// Writes a sample file to disk under the slot's upload directory and
    /// queues a <see cref="SlotDocument"/> row on the supplied context.
    /// The caller is expected to call SaveChangesAsync after the batch.
    /// </summary>
    private void SeedSampleSlotDocument(
        int slotId,
        string fileName,
        string content,
        string category,
        string description,
        string uploaderUserId,
        string contentType,
        ApplicationDbContext db)
    {
        var dirAbs = _uploadPaths.GetSlotUploadsRoot(slotId);
        var fileAbs = Path.Combine(dirAbs, fileName);
        File.WriteAllText(fileAbs, content);
        db.SlotDocuments.Add(new SlotDocument
        {
            ServiceSlotId = slotId,
            Title = Path.GetFileNameWithoutExtension(fileName),
            Description = description,
            Category = category,
            FilePath = _uploadPaths.GetSlotRelativePath(slotId, fileName),
            OriginalFileName = fileName,
            ContentType = contentType,
            SizeBytes = new FileInfo(fileAbs).Length,
            UploadedByUserId = uploaderUserId,
            UploadedUtc = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// Returns (thisSunday, nextSunday) at 14:00 UTC. If the current UTC
    /// time is already past 14:00 UTC on a Sunday, "this Sunday" advances
    /// by 7 days so the demo never starts with assignments in the past.
    /// </summary>
    private static (DateTime thisSunday, DateTime nextSunday) ResolveSundayWindow()
    {
        var today = DateTime.UtcNow.Date;
        int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var thisSunday = today.AddDays(daysUntilSunday).AddHours(14);
        if (thisSunday < DateTime.UtcNow)
        {
            thisSunday = thisSunday.AddDays(7);
        }
        return (thisSunday, thisSunday.AddDays(7));
    }

    /// <summary>
    /// Returns (lastSaturday, thisSaturday, nextSaturday) all at 9:00 UTC.
    /// "this Saturday" advances by 7 days if the current time is past 9:00 UTC
    /// on a Saturday, so the demo never starts with games in the past. The
    /// last-Saturday anchor gives the standings table a played game for the
    /// demo to render.
    /// </summary>
    private static (DateTime lastSaturday, DateTime thisSaturday, DateTime nextSaturday) ResolveSaturdayWindow()
    {
        var today = DateTime.UtcNow.Date;
        int daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
        var thisSaturday = today.AddDays(daysUntilSaturday).AddHours(9);
        if (thisSaturday < DateTime.UtcNow)
        {
            thisSaturday = thisSaturday.AddDays(7);
        }
        return (thisSaturday.AddDays(-7), thisSaturday, thisSaturday.AddDays(7));
    }

    private async Task<IdentityUser> EnsureUserAsync(string email, string password, CancellationToken ct)
    {
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Self-healing: if a previous seed run produced a user without a
            // PasswordHash (an older bug let this slip through), set the
            // canonical demo password. AddPasswordAsync is the supported way
            // to put a password on a user that doesn't have one; it throws
            // if a password is already set, which is fine because the
            // PasswordHash-null check is our gate.
            if (string.IsNullOrEmpty(existing.PasswordHash))
            {
                _log.LogWarning("DatabaseSeeder: user {Email} has no PasswordHash; setting canonical demo password.", email);
                var add = await _userManager.AddPasswordAsync(existing, password);
                if (!add.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"DatabaseSeeder: failed to set password for {email}: {string.Join("; ", add.Errors.Select(e => e.Description))}");
                }
            }
            return existing;
        }

        var u = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(u, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"DatabaseSeeder: failed to create {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
        return u;
    }
}
