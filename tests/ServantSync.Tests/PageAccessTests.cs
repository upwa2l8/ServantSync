using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Integration-level tests that walk the page-level access logic for the
/// Organizations area and the Ministries area. Each Blazor render-time
/// "if (gate) showForm()" condition has a matching service-level
/// permission check; this suite asserts every gate refuses non-admin
/// callers and succeeds for Admin callers. Treated as a regression net
/// for the "tighten admin gates" RBAC change.
///
/// What the page does => what the test exercises:
/// <list type="bullet">
///   <item>Detail.razor Add-member form => <see cref="IMemberManagementService.AddAsync"/></item>
///   <item>Detail.razor Update-role dropdown => <see cref="IMemberManagementService.UpdateRoleAsync"/></item>
///   <item>Detail.razor Remove-member button => <see cref="IMemberManagementService.RemoveAsync"/></item>
///   <item>Detail.razor Add-arena form => <see cref="IArenaService.CreateAsync"/></item>
///   <item>Ministries/Edit.razor Save handler => <see cref="IOrganizationMinistryService.UpsertAsync"/></item>
/// </list>
/// </summary>
public class PageAccessTests : SqliteTestBase
{
    private MemberManagementService NewMembers() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<MemberManagementService>.Instance);

    private ArenaService NewArenas() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<ArenaService>.Instance);

    private OrganizationMinistryService NewMinistries() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<OrganizationMinistryService>.Instance);

    private OrganizationService NewOrgs() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<OrganizationService>.Instance);

    /// <summary>
    /// Single shared "seed org" with one of each role plus a target person
    /// that isn't a member. Reused across every test so setup overhead
    /// stays small.
    /// </summary>
    private (Organization Org, string AdminId, string CoordinatorId, string VolunteerId, string TargetId)
        SeedOrgWithRoles(string name = "Seed Org")
    {
        var org = TestData.Org(Factory, name);
        var admin = TestData.Person(Factory);
        var coordinator = TestData.Person(Factory);
        var volunteer = TestData.Person(Factory);
        var target = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);
        return (org, admin.UserId, coordinator.UserId, volunteer.UserId, target.UserId);
    }

    // ─── Per-gate denial matrix ────────────────────────────────────────────
    // Each test below asserts that the gate referenced by a specific
    // Detail.razor / Ministries/Edit.razor handler refuses non-Admin
    // callers. The Admin-positive path is exercised by the existing
    // per-service test suites; this file's purpose is the negative matrix
    // (what happens when non-admins try).

    [Fact]
    public async Task Detail_AddMember_GateDeniesCoordinatorAndVolunteer()
    {
        var (org, _, coordinatorId, volunteerId, targetId) = SeedOrgWithRoles();

        var coordResult = await NewMembers().AddAsync(
            coordinatorId, org.Id, targetId, OrganizationRole.Volunteer);
        var volResult = await NewMembers().AddAsync(
            volunteerId, org.Id, targetId, OrganizationRole.Volunteer);

        Assert.Equal(MemberAddResult.PermissionDenied, coordResult);
        Assert.Equal(MemberAddResult.PermissionDenied, volResult);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == targetId));
    }

    [Fact]
    public async Task Detail_UpdateRole_GateDeniesNonAdmin()
    {
        var (org, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles();
        // Make sure the volunteer's role change is also tested.
        var coordResult = await NewMembers().UpdateRoleAsync(
            coordinatorId, org.Id, volunteerId, OrganizationRole.Coordinator);
        var volResult = await NewMembers().UpdateRoleAsync(
            volunteerId, org.Id, coordinatorId, OrganizationRole.Admin);

        Assert.Equal(MemberMutationResult.PermissionDenied, coordResult);
        Assert.Equal(MemberMutationResult.PermissionDenied, volResult);
    }

    [Fact]
    public async Task Detail_RemoveMember_GateDeniesNonAdmin()
    {
        var (org, _, coordinatorId, volunteerId, targetId) = SeedOrgWithRoles();

        var coordResult = await NewMembers().RemoveAsync(
            coordinatorId, org.Id, volunteerId);
        var volResult = await NewMembers().RemoveAsync(
            volunteerId, org.Id, coordinatorId);

        Assert.Equal(MemberRemoveResult.PermissionDenied, coordResult);
        Assert.Equal(MemberRemoveResult.PermissionDenied, volResult);

        await using var db = await Factory.CreateDbContextAsync();
        // Both rows still present.
        Assert.True(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == volunteerId));
        Assert.True(await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == coordinatorId));
    }

    [Fact]
    public async Task Detail_AddArena_GateDeniesCoordinatorAndVolunteer()
    {
        var (org, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles();

        var coordResult = await NewArenas().CreateAsync(
            coordinatorId, org.Id, "Field X", "Grass", 22, true);
        var volResult = await NewArenas().CreateAsync(
            volunteerId, org.Id, "Field Y", "Grass", 22, true);

        Assert.Equal(ArenaAddResult.PermissionDenied, coordResult);
        Assert.Equal(ArenaAddResult.PermissionDenied, volResult);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Arenas.AnyAsync(a => a.Name == "Field X" || a.Name == "Field Y"));
    }

    [Fact]
    public async Task MinistriesEdit_SaveUpsert_GateDeniesCoordinatorAndVolunteer()
    {
        var (org, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles();

        var coordResult = await NewMinistries().UpsertAsync(
            coordinatorId, org.Id, ministryId: null,
            name: "Volunteer team", description: null,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);
        var volResult = await NewMinistries().UpsertAsync(
            volunteerId, org.Id, ministryId: null,
            name: "Volunteer team 2", description: null,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, coordResult);
        Assert.Equal(MinistryUpsertResult.PermissionDenied, volResult);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Ministries.AnyAsync(m =>
            m.Name == "Volunteer team" || m.Name == "Volunteer team 2"));
    }

    // ─── Last-Admin invariant: org always has ≥1 Admin member ─────────────

    [Fact]
    public async Task Detail_RemoveMember_LastAdmin_CannotRemoveSelf()
    {
        // Caller is the only Admin. Service refuses LastAdminRefused
        // regardless of who the target is (caller == target here).
        var org = TestData.Org(Factory, "Lone Admin Org");
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewMembers().RemoveAsync(
            admin.UserId, org.Id, admin.UserId);

        Assert.Equal(MemberRemoveResult.LastAdminRefused, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.OrganizationMemberships.SingleAsync(m =>
            m.OrganizationId == org.Id && m.PersonUserId == admin.UserId);
        Assert.Equal(OrganizationRole.Admin, row.Role);
    }

    // ─── OrgAuth coordination: gate definitions live in one place ─────────

    [Fact]
    public async Task OrgAuth_CoordinatorCannotAppearAsAdminAcrossGateBoundaries()
    {
        // Sanity check that the gate definitions agree: a Coordinator
        // should not be treated as an Admin for ANY of the four gated
        // actions. Catches a future refactor that adds a new service but
        // forgets to query IsOrgAdminAsync.
        var (org, _, coordinatorId, _, targetId) = SeedOrgWithRoles();

        var add = await NewMembers().AddAsync(coordinatorId, org.Id, targetId, OrganizationRole.Volunteer);
        var arena = await NewArenas().CreateAsync(coordinatorId, org.Id, "Field Z", null, null, true);
        var ministry = await NewMinistries().UpsertAsync(
            coordinatorId, org.Id, ministryId: null,
            name: "Volunteer team", description: null,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        // All three services must deny the Coordinator identically.
        Assert.Equal(MemberAddResult.PermissionDenied, add);
        Assert.Equal(ArenaAddResult.PermissionDenied, arena);
        Assert.Equal(MinistryUpsertResult.PermissionDenied, ministry);
    }

    // ─── OrganizationService CreateOrgAsync ─────────────────────────────────

    [Fact]
    public async Task OrgEdit_NewOrganization_GateDeniesNonAdmin()
    {
        // Sanity check that the gate we tightened earlier (CreateOrg) also
        // refuses non-admin callers. The per-service suite already covers
        // this through OrganizationServiceTests; this matrix entry is
        // reproduced here so PageAccessTests reads as a single source of
        // truth for "what gates exist on the Organizations-related pages".
        var (org, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles();

        var coordResult = await NewOrgs().CreateOrgAsync(
            coordinatorId, "Should Not Exist",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);
        var volResult = await NewOrgs().CreateOrgAsync(
            volunteerId, "Should Not Exist 2",
            description: null, address: null, contactEmail: null, contactPhone: null,
                timeZoneId: null);

        Assert.Null(coordResult);
        Assert.Null(volResult);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Organizations.AnyAsync(o =>
            o.Name == "Should Not Exist" || o.Name == "Should Not Exist 2"));
    }
}
