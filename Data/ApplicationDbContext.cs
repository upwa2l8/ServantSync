using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServantSync.Models;

namespace ServantSync.Data;

/// <summary>
/// Application DbContext combining ASP.NET Core Identity tables with all
/// volunteer-database domain tables. Configured via
/// <see cref="IDbContextFactory{TContext}"/> for safe per-operation use in
/// Blazor Interactive Server (long-lived SignalR circuits).
/// </summary>
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Ministry> Ministries => Set<Ministry>();
    public DbSet<MinistryInterest> MinistryInterests => Set<MinistryInterest>();
    public DbSet<ServiceSlot> ServiceSlots => Set<ServiceSlot>();
    public DbSet<SlotOccurrence> SlotOccurrences => Set<SlotOccurrence>();
    public DbSet<TrainingContent> TrainingContents => Set<TrainingContent>();
    public DbSet<TrainingRequirement> TrainingRequirements => Set<TrainingRequirement>();
    public DbSet<TrainingCompletion> TrainingCompletions => Set<TrainingCompletion>();
    public DbSet<TrainingActivity> TrainingActivities => Set<TrainingActivity>();
    // Round-FR-2: in-person training sessions + volunteer sign-ups.
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<TrainingSessionAttendee> TrainingSessionAttendees => Set<TrainingSessionAttendee>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Arena> Arenas => Set<Arena>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<SlotDocument> SlotDocuments => Set<SlotDocument>();
    // Round-BC SystemAdmin grant/revoke audit log. Append-only by
    // application convention (no service updates a row, no routine
    // purges). See Services/SystemAdminManagementService.cs.
    public DbSet<SystemAdminGrantAudit> SystemAdminGrantAudits => Set<SystemAdminGrantAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Person (PK/FK = IdentityUser.Id) ----
        modelBuilder.Entity<Person>(b =>
        {
            b.HasKey(p => p.UserId);
            b.HasOne(p => p.User)
                .WithOne()
                .HasForeignKey<Person>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => p.LastName);
        });

        // ---- Organization ----
        modelBuilder.Entity<Organization>(b =>
        {
            b.HasIndex(o => o.Name);
            // RegistrationToken is set by OrganizationService on Create and
            // rotated by GenerateRegistrationTokenAsync. Indexed unique so a
            // rotated/new token can't collide with another org's token; SQLite
            // treats multiple NULLs as distinct so existing rows don't
            // conflict before the first rotation.
            b.HasIndex(o => o.RegistrationToken).IsUnique();
        });

        // ---- OrganizationMembership (unique per (Person, Org)) ----
        modelBuilder.Entity<OrganizationMembership>(b =>
        {
            b.HasIndex(m => new { m.PersonUserId, m.OrganizationId }).IsUnique();
            b.HasOne(m => m.Person)
                .WithMany(p => p.Memberships)
                .HasForeignKey(m => m.PersonUserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.Organization)
                .WithMany(o => o.Memberships)
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Ministry ----
        modelBuilder.Entity<Ministry>(b =>
        {
            b.HasOne(m => m.Organization)
                .WithMany(o => o.Ministries)
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.CoordinatorPerson)
                .WithMany()
                .HasForeignKey(m => m.CoordinatorPersonUserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(m => m.ParentMinistry)
                .WithMany(p => p.SubMinistries)
                .HasForeignKey(m => m.ParentMinistryId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(m => new { m.OrganizationId, m.Name }).IsUnique();
            b.HasIndex(m => m.ParentMinistryId);
        });

        // ---- MinistryInterest (person's volunteered-interest preference) ----
        // Distinct from OrganizationMembership: this is a soft signal, not a
        // RBAC role. Cascade-deletes so removing a ministry (or its parent
        // org) sweeps stale interest rows; removing a person sweeps their
        // interest set. No soft-delete / tombstone: preference data has no
        // historical value worth preserving.
        modelBuilder.Entity<MinistryInterest>(b =>
        {
            b.HasIndex(i => new { i.PersonUserId, i.MinistryId }).IsUnique();
            b.HasOne(i => i.Person)
                .WithMany()
                .HasForeignKey(i => i.PersonUserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(i => i.Ministry)
                .WithMany()
                .HasForeignKey(i => i.MinistryId)
                .OnDelete(DeleteBehavior.Cascade);
            // Indexed on PersonUserId alone so ListJoinedAsync (per-user
            // query) is a single index seek rather than a scan.
            b.HasIndex(i => i.PersonUserId);
        });

        // ---- ServiceSlot ----
        modelBuilder.Entity<ServiceSlot>(b =>
        {
            b.HasOne(s => s.Ministry)
                .WithMany(m => m.ServiceSlots)
                .HasForeignKey(s => s.MinistryId)
                .OnDelete(DeleteBehavior.Cascade);
            // Per-slot coordinator FK: SetNull (not Cascade) so deleting a
            // Person row clears the slot's CoordinatorPersonUserId without
            // dropping the slot itself — mirrors Ministry / Team's
            // coordinator FK choice. A coordinator's account being removed
            // should not vaporize their slot's history.
            b.HasOne(s => s.CoordinatorPerson)
                .WithMany()
                .HasForeignKey(s => s.CoordinatorPersonUserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(s => new { s.MinistryId, s.Name }).IsUnique();
            // Capacity lives on ServiceSlot (default 1) so it survives decoupled
            // from any one SlotOccurrence; per-occurrence overrides live on
            // SlotOccurrence.CapacityOverride. The DB-level default keeps old
            // rows consistent if a partial migration runs, but the model
            // initializer in C# (Capacity = 1) is the source of truth for new
            // rows.
            b.Property(s => s.Capacity).HasDefaultValue(1);
        });

        // ---- SlotOccurrence (open-time scaffolding for self-signup) ----
        modelBuilder.Entity<SlotOccurrence>(b =>
        {
            b.HasOne(o => o.ServiceSlot)
                .WithMany(s => s.Occurrences)
                .HasForeignKey(o => o.ServiceSlotId)
                .OnDelete(DeleteBehavior.Cascade);
            // One open shift per (slot, time). Prevents a coordinator from
            // accidentally creating two overlapping shifts for the same
            // opportunity at the same instant.
            b.HasIndex(o => new { o.ServiceSlotId, o.StartUtc }).IsUnique();
            // Window query support: list upcoming occurrences service-wide.
            b.HasIndex(o => o.StartUtc);
        });

        // ---- TrainingContent (per-org catalog) ----
        // Since round N, TrainingContent is owned by exactly one Organization —
        // a single shared "Safe Spaces 101" can exist independently in two
        // orgs. The Title+Version uniqueness was global and broke when the
        // same content showed up in two orgs, so the index is now scoped
        // (OrganizationId, Title, Version). The Organization FK cascades on
        // delete — matching Ministry's policy.
        modelBuilder.Entity<TrainingContent>(b =>
        {
            b.HasOne(c => c.Organization)
                .WithMany(o => o.TrainingContents)
                .HasForeignKey(c => c.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(c => new { c.OrganizationId, c.Title, c.Version }).IsUnique();
            // Catalog query support: filter by org, sort by title.
            b.HasIndex(c => new { c.OrganizationId, c.Title });
        });

        // ---- TrainingRequirement (exactly one scope, enforced in DB) ----
        modelBuilder.Entity<TrainingRequirement>(b =>
        {
            b.ToTable(t => t.HasCheckConstraint(
                "CK_TrainingRequirement_OneScope",
                "(\"OrganizationId\" IS NOT NULL AND \"ServiceSlotId\" IS NULL) " +
                "OR (\"OrganizationId\" IS NULL AND \"ServiceSlotId\" IS NOT NULL)"));
            b.HasOne(r => r.Organization)
                .WithMany(o => o.TrainingRequirements)
                .HasForeignKey(r => r.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.ServiceSlot)
                .WithMany(s => s.TrainingRequirements)
                .HasForeignKey(r => r.ServiceSlotId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.TrainingContent)
                .WithMany()
                .HasForeignKey(r => r.TrainingContentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- TrainingCompletion (round-FR-2: extended with CompletionSource +
        //                              MarkedCompleteByUserId + ManualCompletionNotes) ----
        // Round-FR-2 appended 3 columns. No FK changes — MarkedCompleteByUserId
        // is a plain string column (no FK nav) so the audit trail survives
        // user deletion. The existing Person/TrainingContent FK semantics
        // carry over unchanged.
        modelBuilder.Entity<TrainingCompletion>(b =>
        {
            b.HasIndex(c => new { c.PersonUserId, c.TrainingContentId, c.TrainingContentVersion }).IsUnique();
            b.HasOne(c => c.Person)
                .WithMany(p => p.TrainingCompletions)
                .HasForeignKey(c => c.PersonUserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.TrainingContent)
                .WithMany()
                .HasForeignKey(c => c.TrainingContentId)
                .OnDelete(DeleteBehavior.Restrict);
            // Round-FR-2 column-length audit. EF conventions pick these up
            // from the [StringLength] attributes on the model, but pinning
            // here makes the migration's schema independent of any future
            // attribute drift.
            b.Property(c => c.MarkedCompleteByUserId).HasMaxLength(128);
            b.Property(c => c.ManualCompletionNotes).HasMaxLength(1000);
        });

        // ---- TrainingActivity (round M) ----
        // Per-user progress on a specific content version. One row per
        // (Person, Content, Version) — the activity is reset by content
        // version because the admin bumping the version invalidates the
        // previous engagement (the volunteer re-engages before
        // re-completing). Cascade deletes match TrainingCompletion so
        // pulling the user or the content sweeps orphan activity rows.
        modelBuilder.Entity<TrainingActivity>(b =>
        {
            b.HasIndex(a => new { a.PersonUserId, a.TrainingContentId, a.TrainingContentVersion }).IsUnique();
            b.HasOne(a => a.Person)
                .WithMany()
                .HasForeignKey(a => a.PersonUserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(a => a.TrainingContent)
                .WithMany()
                .HasForeignKey(a => a.TrainingContentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- TrainingSession (round-FR-2) ----
        // In-person training event tied to an org (matches TrainingContent's
        // org-scoped policy). CreatedByUserId is a plain string column with
        // no FK — mirrors SystemAdminGrantAudit's "audit trail outlives the
        // actor" pattern so deleting the coordinator who created a session
        // does NOT vaporize the session's history.
        modelBuilder.Entity<TrainingSession>(b =>
        {
            b.HasOne(s => s.Organization)
                .WithMany()
                .HasForeignKey(s => s.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);  // drop sessions when the org is deleted
            b.HasOne(s => s.TrainingContent)
                .WithMany()
                .HasForeignKey(s => s.TrainingContentId)
                .OnDelete(DeleteBehavior.SetNull);  // preserve session history if content is removed
            b.Property(s => s.Title).HasMaxLength(200);
            b.Property(s => s.Location).HasMaxLength(200);
            // Index optimized for the "list upcoming for org" hot path
            // (TrainingSessionService.ListUpcomingAsync).
            b.HasIndex(s => new { s.OrganizationId, s.StartUtc });
            b.HasIndex(s => s.Status);
        });

        // ---- TrainingSessionAttendee (round-FR-2) ----
        // Volunteer sign-up row. Person FK cascades to match
        // OrganizationMembership (deleting a Person sweeps their session
        // sign-ups). TrainingSession FK cascades so deleting a session
        // sweeps its attendee list. The audit-trail concern (marker
        // identity + notes) lives on the TrainingCompletion rows whose
        // MarkedCompleteByUserId is a plain string column (no FK) — so
        // deletion of a volunteer or their coordinator doesn't erase
        // the audit trail from completion rows that have already been
        // written.
        modelBuilder.Entity<TrainingSessionAttendee>(b =>
        {
            b.HasOne(a => a.TrainingSession)
                .WithMany(s => s.Attendees)
                .HasForeignKey(a => a.TrainingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(a => a.Person)
                .WithMany()
                .HasForeignKey(a => a.PersonUserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Composite-unique enforces one-signup-per-volunteer (a volunteer
            // can't double-sign-up for the same session).
            b.HasIndex(a => new { a.TrainingSessionId, a.PersonUserId }).IsUnique();
            // Index for the "list this person's upcoming sessions" query path.
            b.HasIndex(a => a.PersonUserId);
        });

        // ---- Assignment (conflict-detection hot path) ----
        modelBuilder.Entity<Assignment>(b =>
        {
            b.HasIndex(a => new { a.PersonUserId, a.StartUtc });
            b.HasIndex(a => new { a.ServiceSlotId, a.StartUtc });
            b.HasOne(a => a.Person)
                .WithMany()
                .HasForeignKey(a => a.PersonUserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(a => a.ServiceSlot)
                .WithMany(s => s.Assignments)
                .HasForeignKey(a => a.ServiceSlotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Arena (org-scoped, shared by all leagues in the org) ----
        modelBuilder.Entity<Arena>(b =>
        {
            b.HasOne(a => a.Organization)
                .WithMany()
                .HasForeignKey(a => a.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(a => new { a.OrganizationId, a.Name }).IsUnique();
        });

        // ---- Team (belongs to a league ministry) ----
        modelBuilder.Entity<Team>(b =>
        {
            b.HasOne(t => t.Ministry)
                .WithMany(m => m.Teams)
                .HasForeignKey(t => t.MinistryId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(t => t.CoachPerson)
                .WithMany()
                .HasForeignKey(t => t.CoachPersonUserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(t => new { t.MinistryId, t.Name }).IsUnique();
        });

        // ---- Player (roster row, may link to a parent Person) ----
        modelBuilder.Entity<Player>(b =>
        {
            b.HasOne(p => p.Team)
                .WithMany(t => t.Players)
                .HasForeignKey(p => p.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(p => p.PrimaryContactPerson)
                .WithMany()
                .HasForeignKey(p => p.PrimaryContactPersonUserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(p => p.TeamId);
        });

        // ---- Game (scheduled match: league + 2 teams + arena) ----
        modelBuilder.Entity<Game>(b =>
        {
            b.HasOne(g => g.Ministry)
                .WithMany()
                .HasForeignKey(g => g.MinistryId)
                .OnDelete(DeleteBehavior.Restrict);  // preserve game history if a league is deleted
            b.HasOne(g => g.HomeTeam)
                .WithMany(t => t.HomeGames)
                .HasForeignKey(g => g.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(g => g.AwayTeam)
                .WithMany(t => t.AwayGames)
                .HasForeignKey(g => g.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(g => g.Arena)
                .WithMany(a => a.Games)
                .HasForeignKey(g => g.ArenaId)
                .OnDelete(DeleteBehavior.Restrict);
            // Index optimized for the arena-conflict check
            // (overlapping non-terminal games at the same arena).
            b.HasIndex(g => new { g.ArenaId, g.StartUtc });
            b.HasIndex(g => new { g.MinistryId, g.StartUtc });
        });

        // ---- SlotDocument (per-slot shared documents for volunteers) ----
        modelBuilder.Entity<SlotDocument>(b =>
        {
            b.HasOne(d => d.ServiceSlot)
                .WithMany()
                .HasForeignKey(d => d.ServiceSlotId)
                .OnDelete(DeleteBehavior.Cascade);  // drop docs when the slot is deleted
            // FK chain: SlotDocuments.UploadedByUserId → People.UserId (PK of
            // Person, also FK to AspNetUsers.Id). EF supports this
            // "shared-key FK" pattern; the principal is People, not AspNetUsers.
            b.HasOne(d => d.UploadedByPerson)
                .WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);  // preserve doc history if uploader is deleted
            // Indexed for the grouped-list query (per-slot, ordered by category).
            b.HasIndex(d => new { d.ServiceSlotId, d.Category });
        });

        // ---- SystemAdminGrantAudit (round-BC) ----
        // Append-only log row. No FK to AspNetUsers on ActorUserId /
        // TargetUserId because a deleted user must NOT cascade-delete
        // their audit history — the forensics view must outlive the
        // user. Identity rows cascade-delete; the audit log is
        // intentionally NOT wired to cascade so a SysAdmin removing
        // their own account can never erase their own grant history.
        modelBuilder.Entity<SystemAdminGrantAudit>(b =>
        {
            b.HasIndex(a => a.TimestampUtc);
            b.Property(a => a.Reason).HasMaxLength(80);
        });
    }
}
