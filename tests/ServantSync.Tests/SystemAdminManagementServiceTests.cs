using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="SystemAdminManagementService"/>.
/// Covers the round-BC grant/revoke gate via the round-AW
/// <see cref="OrgAuthService.IsSystemAdminAsync"/> foundation. Every
/// grant / revoke attempt (success OR failure) lands a
/// <see cref="SystemAdminGrantAudit"/> row so the audit-log
/// invariant is pinned alongside the gate contract.
///
/// Real SQLite-backed DbContext via the shared
/// <see cref="SqliteTestBase"/>. No Identity UserManager dependency
/// (the service uses direct EF against Identity rows for parity with
/// DatabaseSeeder.SeedSystemAdminRoleAsync's no-UserManager path).
/// </summary>
public class SystemAdminManagementServiceTests : SqliteTestBase
{
    private SystemAdminManagementService NewSvc() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<SystemAdminManagementService>.Instance);

    // ─── Grant ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_SysAdminCaller_FreshTarget_ReturnsSuccess_AndLiftsRole()
    {
        var actor = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        // Identity/System Admin seed: ensure role row + actor's join row.
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);
        // EnsureIdentityUser in TestData.Person already covered target.

        var result = await NewSvc().GrantAsync(actor.UserId, target.UserId);

        Assert.Equal(SystemAdminGrantResult.Success, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstAsync(r => r.NormalizedName == "SYSTEMADMIN");
        Assert.True(await db.UserRoles.AnyAsync(ur =>
            ur.UserId == target.UserId && ur.RoleId == sysAdminRole.Id));
    }

    [Fact]
    public async Task Grant_SysAdminCaller_OnSelf_ReturnsAlreadyHasRole_AndDoesNotDuplicate()
    {
        // Self-grant (actor == target, actor IS SysAdmin): the target
        // — which IS the caller — already holds the role, so the
        // service refuses with AlreadyHasRole rather than inserting a
        // duplicate IdentityUserRole row. Pins the actor-target
        // overlap with the symmetric Revoke_Self_ReturnsSelfRevokeRefused
        // case (which throws Refused because removing your own role
        // would lock you out — but adding it again is a fine no-op).
        var actor = TestData.Person(Factory, "Solo", "Admin", userId: "solo-admin-id");
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        var result = await NewSvc().GrantAsync(actor.UserId, actor.UserId);

        Assert.Equal(SystemAdminGrantResult.AlreadyHasRole, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstAsync(r => r.NormalizedName == "SYSTEMADMIN");
        // Exactly one join row — no duplicate insert.
        Assert.Equal(1, await db.UserRoles.CountAsync(ur =>
            ur.UserId == actor.UserId && ur.RoleId == sysAdminRole.Id));
    }

    [Fact]
    public async Task Grant_NotSysAdmin_ReturnsPermissionDenied_AndDoesNotTouchUserRoles()
    {
        var actor = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        // Do NOT SeedSystemAdminRoleAsync for actor.

        var result = await NewSvc().GrantAsync(actor.UserId, target.UserId);

        Assert.Equal(SystemAdminGrantResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "SYSTEMADMIN");
        if (sysAdminRole is null) return; // role row not seeded yet → no UserRoles can exist
        Assert.False(await db.UserRoles.AnyAsync(ur => ur.UserId == target.UserId));
    }

    [Fact]
    public async Task Grant_EmptyCaller_ReturnsPermissionDenied()
    {
        // Anonymous caller (empty claim from Blazor pre-auth). The
        // empty-userId sentinel must short-circuit before any
        // Identity role lookup.
        var target = TestData.Person(Factory);
        var result = await NewSvc().GrantAsync("", target.UserId);
        Assert.Equal(SystemAdminGrantResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Grant_EmptyTarget_ReturnsTargetUserNotFound()
    {
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        var result = await NewSvc().GrantAsync(actor.UserId, "");

        Assert.Equal(SystemAdminGrantResult.TargetUserNotFound, result);
    }

    [Fact]
    public async Task Grant_NonexistentTarget_ReturnsTargetUserNotFound()
    {
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        var result = await NewSvc().GrantAsync(actor.UserId, targetUserId: "ghost-user-id");

        Assert.Equal(SystemAdminGrantResult.TargetUserNotFound, result);
    }

    [Fact]
    public async Task Grant_AlreadySysAdmin_ReturnsAlreadyHasRole_AndDoesNotDuplicate()
    {
        var actor = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        // Promote BOTH actor AND target initially.
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);
        await TestData.SeedSystemAdminRoleAsync(Factory, target.UserId);

        // Actor tries to grant again — should be an idempotent refusal
        // because the target already holds it.
        var result = await NewSvc().GrantAsync(actor.UserId, target.UserId);

        Assert.Equal(SystemAdminGrantResult.AlreadyHasRole, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstAsync(r => r.NormalizedName == "SYSTEMADMIN");
        // Exactly one UserRoles row for the target — no duplicate.
        Assert.Equal(1, await db.UserRoles.CountAsync(ur =>
            ur.UserId == target.UserId && ur.RoleId == sysAdminRole.Id));
    }

    // ─── Revoke ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_SysAdminCaller_TargetHasRole_ReturnsSuccess_AndRemovesUserRole()
    {
        var actor = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);
        await TestData.SeedSystemAdminRoleAsync(Factory, target.UserId);

        var result = await NewSvc().RevokeAsync(actor.UserId, target.UserId);

        Assert.Equal(SystemAdminRevokeResult.Success, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstAsync(r => r.NormalizedName == "SYSTEMADMIN");
        Assert.False(await db.UserRoles.AnyAsync(ur =>
            ur.UserId == target.UserId && ur.RoleId == sysAdminRole.Id));
        // Actor's row still present.
        Assert.True(await db.UserRoles.AnyAsync(ur =>
            ur.UserId == actor.UserId && ur.RoleId == sysAdminRole.Id));
    }

    [Fact]
    public async Task Revoke_NotSysAdmin_ReturnsPermissionDenied_AndPreservesTargetRole()
    {
        var target = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, target.UserId);
        var stranger = TestData.Person(Factory); // no SystemAdmin role

        var result = await NewSvc().RevokeAsync(stranger.UserId, target.UserId);

        Assert.Equal(SystemAdminRevokeResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstAsync(r => r.NormalizedName == "SYSTEMADMIN");
        Assert.True(await db.UserRoles.AnyAsync(ur =>
            ur.UserId == target.UserId && ur.RoleId == sysAdminRole.Id));
    }

    [Fact]
    public async Task Revoke_EmptyCaller_ReturnsPermissionDenied()
    {
        var target = TestData.Person(Factory);
        var result = await NewSvc().RevokeAsync("", target.UserId);
        Assert.Equal(SystemAdminRevokeResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Revoke_EmptyTarget_ReturnsTargetUserNotFound()
    {
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        var result = await NewSvc().RevokeAsync(actor.UserId, "");

        Assert.Equal(SystemAdminRevokeResult.TargetUserNotFound, result);
    }

    [Fact]
    public async Task Revoke_NonexistentTarget_ReturnsTargetUserNotFound()
    {
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        var result = await NewSvc().RevokeAsync(actor.UserId, targetUserId: "ghost-user-id");

        Assert.Equal(SystemAdminRevokeResult.TargetUserNotFound, result);
    }

    [Fact]
    public async Task Revoke_TargetLacksRole_ReturnsDoesNotHaveRole()
    {
        var actor = TestData.Person(Factory);
        var noRoleTarget = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);
        // Deliberately don't grant SystemAdmin to noRoleTarget.

        var result = await NewSvc().RevokeAsync(actor.UserId, noRoleTarget.UserId);

        Assert.Equal(SystemAdminRevokeResult.DoesNotHaveRole, result);
    }

    [Fact]
    public async Task Revoke_Self_ReturnsSelfRevokeRefused_AndPreservesSelfRole()
    {
        // Critical safety pin: an actor must NOT be able to revoke their
        // own SystemAdmin role. The page-level button is also disabled
        // when row.userId == caller.userId, but the SERVICE is the
        // unit-testable source of truth.
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        var result = await NewSvc().RevokeAsync(actor.UserId, actor.UserId);

        Assert.Equal(SystemAdminRevokeResult.SelfRevokeRefused, result);

        await using var db = await Factory.CreateDbContextAsync();
        var sysAdminRole = await db.Roles.FirstAsync(r => r.NormalizedName == "SYSTEMADMIN");
        Assert.True(await db.UserRoles.AnyAsync(ur =>
            ur.UserId == actor.UserId && ur.RoleId == sysAdminRole.Id));
    }

    // ─── Audit log invariant ─────────────────────────────────────────────
    // Every grant / revoke attempt (success OR failure) lands an audit
    // row so ops can investigate who tried what. The pin here: a single
    // Grant attempt = exactly one audit row, NOT zero (which would
    // mean "the system silently swallowed the refusal") and NOT two
    // (which would mean a future refactor accidentally double-writes).
    // A regression in any direction is observable in the audit log.

    [Fact]
    public async Task Grant_LandsAuditRow_WithCorrectSuccessAndReason()
    {
        var actor = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        await NewSvc().GrantAsync(actor.UserId, target.UserId);

        await using var db = await Factory.CreateDbContextAsync();
        var rows = await db.SystemAdminGrantAudits
            .Where(a => a.ActorUserId == actor.UserId && a.TargetUserId == target.UserId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.True(rows[0].Success);
        Assert.Equal(SystemAdminAuditAction.Grant, rows[0].Action);
        Assert.Null(rows[0].Reason); // success row has no reason
    }

    [Fact]
    public async Task Grant_Refused_LandsAuditRow_WithReasonAndSuccessFalse()
    {
        var actor = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        // actor NOT a SysAdmin → refusal.

        await NewSvc().GrantAsync(actor.UserId, target.UserId);

        await using var db = await Factory.CreateDbContextAsync();
        var rows = await db.SystemAdminGrantAudits
            .Where(a => a.ActorUserId == actor.UserId && a.TargetUserId == target.UserId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.False(rows[0].Success);
        Assert.Equal("PermissionDenied", rows[0].Reason);
    }

    [Fact]
    public async Task Revoke_SelfRevoke_LandsAuditRow_WithSelfRevokeRefusedReason()
    {
        // Critical operational forensics: the actor's botched
        // self-revoke attempt lands a row so ops can spot patterns.
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        await NewSvc().RevokeAsync(actor.UserId, actor.UserId);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.SystemAdminGrantAudits
            .SingleAsync(a => a.ActorUserId == actor.UserId && a.TargetUserId == actor.UserId);
        Assert.False(row.Success);
        Assert.Equal("SelfRevokeRefused", row.Reason);
        Assert.Equal(SystemAdminAuditAction.Revoke, row.Action);
    }

    // ─── List / ListRecentAudits ROs ──────────────────────────────────────

    [Fact]
    public async Task List_ReturnsEverySysAdmin_AsRowsWithEmailDisplayName()
    {
        var a = TestData.Person(Factory, "Alpha", "Admin", userId: "alpha-id");
        var b = TestData.Person(Factory, "Beta", "Boss", userId: "beta-id");
        await TestData.SeedSystemAdminRoleAsync(Factory, a.UserId);
        await TestData.SeedSystemAdminRoleAsync(Factory, b.UserId);

        var rows = await NewSvc().ListAsync();

        Assert.Equal(2, rows.Count);
        var alphaRow = rows.Single(r => r.UserId == a.UserId);
        Assert.Equal("Alpha Admin", alphaRow.DisplayName);
        Assert.Equal("alpha-id@test.local", alphaRow.Email);
    }

    [Fact]
    public async Task List_NoSysAdmins_ReturnsEmpty()
    {
        // Sanity: a fresh DB with no SystemAdmin promotions returns
        // empty rather than throwing — the page renders the
        // "Nobody holds the SystemAdmin role" empty state.
        var rows = await NewSvc().ListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ListRecentAudits_ReturnsNewestFirst_AndCapsAt50()
    {
        var actor = TestData.Person(Factory);
        await TestData.SeedSystemAdminRoleAsync(Factory, actor.UserId);

        // Insert 60 audit rows so the cap engages.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            for (int i = 0; i < 60; i++)
            {
                db.SystemAdminGrantAudits.Add(new SystemAdminGrantAudit
                {
                    ActorUserId = actor.UserId,
                    TargetUserId = actor.UserId,
                    Action = SystemAdminAuditAction.Grant,
                    Success = true,
                    TimestampUtc = DateTime.UtcNow.AddSeconds(-i), // newest first
                });
            }
            await db.SaveChangesAsync();
        }

        var rows = await NewSvc().ListRecentAuditsAsync();

        Assert.Equal(50, rows.Count); // capped
        // Newest-first ordering: the rows[0] TimestampUtc should be the
        // most recent (i=0 → UtcNow + 0s).
        Assert.True(rows[0].TimestampUtc > rows[1].TimestampUtc);
    }

    [Fact]
    public async Task ListRecentAudits_EmptyDb_ReturnsEmpty()
    {
        var rows = await NewSvc().ListRecentAuditsAsync();
        Assert.Empty(rows);
    }
}
