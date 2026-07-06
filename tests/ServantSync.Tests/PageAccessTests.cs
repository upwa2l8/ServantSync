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

    private SlotManagementService NewSlots() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<SlotManagementService>.Instance);

    /// <summary>
    /// Round-FR-3.3 helper: PersonService factory identical to the
    /// PersonServiceTests.NewService shape. Needs the real
    /// <see cref="SqliteTestBase.UserManager"/> because
    /// CreateStubAsync / ClaimStubAsync / RotateClaimTokenAsync all
    /// touch the Identity user table (placeholder IdentityUser mint,
    /// password-hash checks, etc.). Uses the real <see cref="OrgAuthService"/>
    /// so the gate the test asserts is the production gate, not a
    /// shim — important because PersonService re-checks the gate on
    /// every method and a shim-meddling gate would silently let
    /// non-admin callers slip through.
    /// </summary>
    private PersonService NewPersonService() => new(
        Factory,
        UserManager,
        new OrgAuthService(Factory),
        NullLogger<PersonService>.Instance);

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

    // ─── Round-BA: ServiceSlots/Edit.razor gate split ─────────────────────
    // Round-BA moved the single CanManageOrgAsync gate on the slot Edit
    // page into a tier-split gate:
    //   * Create path (slotId = null)  → CanManageMinistryAsync(MinId)
    //   * Edit path   (slotId = int)   → CanManageSlotAsync(slotId)
    // SlotManagementService.UpsertAsync is the source of truth for both
    // gates (the page re-checks them but the service is what gets
    // exercised in this matrix). Tests assert the gate contract for each
    // tier; the positive admin path is already covered by the per-service
    // suite and would be redundant here.

    [Fact]
    public async Task Edit_NewSlot_AllowsMinistryCoordinator()
    {
        // Widen-on-create: an org Coordinator (no Admin role) can create
        // a slot in any ministry they CanManageMinistryAsync-gate. Pins
        // the round-BA split: previously the page's CanManageOrgAsync
        // gate ALSO accepted org Coordinators; the new ministry-tier
        // gate keeps that property AND additionally accepts ministry
        // Coordinators (so the order-side PromoteSlot flow keeps
        // working without Admin escalation).
        var (org, _, coordinatorId, _, _) = SeedOrgWithRoles();
        var ministry = TestData.Ministry(Factory, org.Id, "Worship");

        var result = await NewSlots().UpsertAsync(
            coordinatorId, org.Id, ministry.Id, slotId: null,
            name: "New Slot for Coordinator", description: null,
            location: null, defaultDurationMinutes: 0,
            isActive: true, capacity: 1,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(SlotUpsertResult.Saved, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.ServiceSlots.AnyAsync(s =>
            s.MinistryId == ministry.Id && s.Name == "New Slot for Coordinator"));
    }

    [Fact]
    public async Task Edit_NewSlot_DeniesRandomVolunteer()
    {
        // Negative matrix: a Volunteer who's a member of the org but
        // holds neither Admin nor Coordinator role must still be denied.
        var (org, _, _, volunteerId, _) = SeedOrgWithRoles();
        var ministry = TestData.Ministry(Factory, org.Id, "Kids");

        var result = await NewSlots().UpsertAsync(
            volunteerId, org.Id, ministry.Id, slotId: null,
            name: "Should Not Exist", description: null,
            location: null, defaultDurationMinutes: 0,
            isActive: true, capacity: 1,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(SlotUpsertResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.ServiceSlots.AnyAsync(s => s.Name == "Should Not Exist"));
    }

    [Fact]
    public async Task Edit_ExistingSlot_AllowsSlotCoordinator()
    {
        // The round-BA edit-path widens to slot-tier: a person who IS the
        // slot's own CoordinatorPersonUserId can edit their slot even
        // when they hold zero org-wide role (no Admin, no org
        // Coordinator, no ministry Coordinator). This was the substantive
        // motivation for the per-slot coordinator field in the first
        // place — Sara can run Greeters without admins having to
        // micro-manage the ministry layer.
        var (org, _, _, _, _) = SeedOrgWithRoles();
        var ministry = TestData.Ministry(Factory, org.Id, "Welcome");
        var slot = TestData.Slot(Factory, ministry.Id, "Welcome Desk");
        // Slot coordinator is a Person who is NOT in any org role — promote
        // them to membership only as a Person so the FK constraint on
        // FK_ServiceSlots_People_CoordinatorPersonUserId is satisfied.
        var slotCoord = TestData.Person(Factory);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            slot.CoordinatorPersonUserId = slotCoord.UserId;
            db.ServiceSlots.Update(slot);
            await db.SaveChangesAsync();
        }

        var result = await NewSlots().UpsertAsync(
            slotCoord.UserId, org.Id, ministry.Id, slotId: slot.Id,
            name: "Welcome Desk (renamed by slot coord)", description: null,
            location: null, defaultDurationMinutes: 0,
            isActive: true, capacity: 1,
            coordinatorPersonUserId: slotCoord.UserId,
            coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(SlotUpsertResult.Saved, result);

        await using var db2 = await Factory.CreateDbContextAsync();
        var reloaded = await db2.ServiceSlots.SingleAsync(s => s.Id == slot.Id);
        Assert.Equal("Welcome Desk (renamed by slot coord)", reloaded.Name);
        Assert.Equal(slotCoord.UserId, reloaded.CoordinatorPersonUserId);
    }

    [Fact]
    public async Task Edit_ExistingSlot_DeniesRandomVolunteer()
    {
        // Negative matrix: a Volunteer in the org cannot edit a slot in
        // that org's ministry. Their membership is at the org tier but
        // not the ministry or slot tier. The slot-tier gate correctly
        // rejects them.
        var (org, _, _, volunteerId, _) = SeedOrgWithRoles();
        var ministry = TestData.Ministry(Factory, org.Id, "Tech");
        var slot = TestData.Slot(Factory, ministry.Id, "Soundboard");

        var result = await NewSlots().UpsertAsync(
            volunteerId, org.Id, ministry.Id, slotId: slot.Id,
            name: "Should Not Rename", description: null,
            location: null, defaultDurationMinutes: 0,
            isActive: true, capacity: 1,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(SlotUpsertResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        var reloaded = await db.ServiceSlots.SingleAsync(s => s.Id == slot.Id);
        Assert.Equal("Soundboard", reloaded.Name);
    }

    [Fact]
    public async Task Edit_ExistingSlot_StaleMinistry_ReturnsNotFound()
    {
        // Edit-path NotFound pin via the realistic stale-membership
        // scenario: an admin opens Edit for ministry A but the
        // (OrgId, MinId, Id) tuple on the form gets corrupted (or the
        // admin deletes ministry A and a stale Save submit arrives) so
        // the page submits ministryId B + an Id that exists in ministry
        // A only. The gate is org-Admin-wider so it PASSES (admins
        // can manage any ministry in their org), but the DB op's
        // scope check (`s.Id == editId && s.MinistryId == ministryId &&
        // s.Ministry!.OrganizationId == organizationId`) refuses
        // because the slot isn't in ministry B. Result must be
        // NotFound, NOT throw, AND must NOT insert a phantom row.
        //
        // Note: a "ghost slotId 999_999" version of this test would
        // fail it because CanManageSlotAsync returns false on
        // nonexistent-slot (= PermissionDenied, not NotFound). The
        // only reachable NotFound path is "exists in another ministry
        // of the same org" — this is it.
        var (org, adminId, _, _, _) = SeedOrgWithRoles();
        var ministryA = TestData.Ministry(Factory, org.Id, "Original Ministry");
        var ministryB = TestData.Ministry(Factory, org.Id, "Stale Page's Ministry");
        var slot = TestData.Slot(Factory, ministryA.Id, "Real Slot in A");

        var result = await NewSlots().UpsertAsync(
            adminId, org.Id, ministryB.Id, slotId: slot.Id,
            name: "Sneak Saves", description: null,
            location: null, defaultDurationMinutes: 0,
            isActive: true, capacity: 1,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(SlotUpsertResult.NotFound, result);

        // ⚠ This NotFound path depends on org-Admin-covers-all-ministries
        // (CanManageSlotAsync defers to CanManageMinistryAsync which
        // widens via CanManageOrgAsync). If a future RBAC change narrows
        // admin scope so admin can only manage a specific ministry,
        // this assertion would shift to PermissionDenied instead of
        // NotFound — re-evaluate or switch to a stub IOrgAuthService.

        // Pin no-insert: NotFound must NOT have created a new row in
        // ministry B with the attempted name (would be a data-corruption
        // regression if a future refactor moves the Add() above the
        // NotFound check).
        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.ServiceSlots.AnyAsync(s => s.Name == "Sneak Saves"));
    }

    [Fact]
    public async Task Edit_NewSlot_DeniesCoordinatorOfDifferentMinistry()
    {
        // Cross-ministry guard: a ministry Coordinator's grant is
        // ministry-scoped, NOT org-wide. They must NOT be able to create
        // a slot in a DIFFERENT ministry of the same org. Pins the round-BA
        // widen-AND-scope contract: ministry coordinator can create
        // slots in their own ministry (see AllowMinistryCoordinator
        // above) but not in others.
        var (org, _, _, _, _) = SeedOrgWithRoles();
        var ministryA = TestData.Ministry(Factory, org.Id, "A's Ministry");
        var ministryB = TestData.Ministry(Factory, org.Id, "B's Ministry");
        var ministerA = TestData.Person(Factory);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            ministryA.CoordinatorPersonUserId = ministerA.UserId;
            db.Ministries.Update(ministryA);
            await db.SaveChangesAsync();
        }

        var result = await NewSlots().UpsertAsync(
            ministerA.UserId, org.Id, ministryB.Id, slotId: null,
            name: "Sneak Across", description: null,
            location: null, defaultDurationMinutes: 0,
            isActive: true, capacity: 1,
            coordinatorPersonUserId: null, coordinatorEmail: null, coordinatorPhone: null);

        Assert.Equal(SlotUpsertResult.PermissionDenied, result);

        await using var db2 = await Factory.CreateDbContextAsync();
        Assert.False(await db2.ServiceSlots.AnyAsync(s => s.Name == "Sneak Across"));
    }

    // ─── Round-FR-3.3: stub-management admin gates ─────────────────────────
    // Three new admin-only methods landing in round-FR-3.3 (Components/Pages/
    // Organizations/Members/AddStub.razor + Members/Stubs.razor + Detail.razor
    // stub-preview sub-section). All three are gated on IsOrgAdminAsync of
    // the target org. Tests below assert the negative matrix; the
    // Admin-positive paths are exercised in PersonServiceTests.
    //
    // The "foreign OrgAdmin" coefficient in each test is critical: a
    // bug that lets cross-org Admins rotate / list stubs in another org
    // would be invisible to a same-org gate-flip test but easy to
    // catch here. PageAccessTests' job is the negative matrix for the
    // page-routed affordances; the per-service suite's positive tests
    // stay green as the gate reference.

    [Fact]
    public async Task AddStub_GateDeniesCoordinatorAndVolunteer_AndForeignOrgAdmin()
    {
        var (orgA, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles("Org A");
        // Foreign OrgAdmin pin: a second admin in Org B proves the
        // cross-org branch of the gate (foreign-OrgAdmin is treated
        // identically to non-admin).
        var (_, _, otherAdminId, _, _) = SeedOrgWithRoles("Org B");

        var svc = NewPersonService();

        var coordResult = await svc.CreateStubAsync(
            organizationId: orgA.Id,
            firstName: "Coord", lastName: "Attempt",
            email: null, phone: null,
            callerUserId: coordinatorId);
        var volResult = await svc.CreateStubAsync(
            organizationId: orgA.Id,
            firstName: "Vol", lastName: "Attempt",
            email: null, phone: null,
            callerUserId: volunteerId);
        var foreignResult = await svc.CreateStubAsync(
            organizationId: orgA.Id,
            firstName: "Foreign", lastName: "Attempt",
            email: null, phone: null,
            callerUserId: otherAdminId);

        Assert.Equal(StubCreationResult.PermissionDenied, coordResult.Result);
        Assert.Equal(StubCreationResult.PermissionDenied, volResult.Result);
        Assert.Equal(StubCreationResult.PermissionDenied, foreignResult.Result);

        // Pin no-inserts: PermissionDenied must NOT have leaked a stub
        // row + placeholder IdentityUser + token row into the DB. A
        // future refactor that fires the CreateStubAsync body BEFORE
        // the gate would mint IdentityUsers and trip these assertions.
        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.People.AnyAsync(p => p.FirstName == "Coord"
            && p.LastName == "Attempt" && p.IsStub));
        Assert.False(await db.People.AnyAsync(p => p.FirstName == "Vol"
            && p.LastName == "Attempt" && p.IsStub));
        Assert.False(await db.People.AnyAsync(p => p.FirstName == "Foreign"
            && p.LastName == "Attempt" && p.IsStub));
        Assert.False(await db.PersonClaimTokens.AnyAsync());
    }

    [Fact]
    public async Task RotateClaimToken_GateDeniesCoordinatorAndVolunteer_AndForeignOrgAdmin()
    {
        var (orgA, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles("Org A");
        var (_, _, otherAdminId, _, _) = SeedOrgWithRoles("Org B");

        var svc = NewPersonService();

        // Seed a stub in Org A so rotation has a target. Mirrors the
        // minimal shape PersonServiceTests.SeedStubWithToken builds,
        // minus the cryptographic token bits (the gate refuses before
        // any hash check, so a raw new Person is sufficient to test
        // the gate).
        var stubPerson = TestData.Person(Factory, "Sara", "Stub");
        TestData.Membership(Factory, stubPerson.UserId, orgA.Id, OrganizationRole.Volunteer);
        using (var db = Factory.CreateDbContext())
        {
            var p = db.People.First(p => p.UserId == stubPerson.UserId);
            p.IsStub = true;
            db.SaveChanges();
        }

        var coordResult = await svc.RotateClaimTokenAsync(
            organizationId: orgA.Id,
            personUserId: stubPerson.UserId,
            callerUserId: coordinatorId);
        var volResult = await svc.RotateClaimTokenAsync(
            organizationId: orgA.Id,
            personUserId: stubPerson.UserId,
            callerUserId: volunteerId);
        var foreignResult = await svc.RotateClaimTokenAsync(
            organizationId: orgA.Id,
            personUserId: stubPerson.UserId,
            callerUserId: otherAdminId);

        Assert.Equal(TokenRotationResult.PermissionDenied, coordResult.Result);
        Assert.Equal(TokenRotationResult.PermissionDenied, volResult.Result);
        Assert.Equal(TokenRotationResult.PermissionDenied, foreignResult.Result);

        // Pin no-token-add: PermissionDenied must NOT have leaked a
        // new PersonClaimToken row. A future refactor that runs the
        // rotation body before the gate would mint a fresh token for
        // a non-admin caller and trip this assertion.
        await using var db2 = await Factory.CreateDbContextAsync();
        Assert.False(await db2.PersonClaimTokens.AnyAsync());
    }

    [Fact]
    public async Task ListStubs_GateDeniesCoordinatorAndVolunteer_AndForeignOrgAdmin()
    {
        var (orgA, _, coordinatorId, volunteerId, _) = SeedOrgWithRoles("Org A");
        var (_, _, otherAdminId, _, _) = SeedOrgWithRoles("Org B");

        var svc = NewPersonService();

        // Seed a stub in Org A so the list isn't trivially empty
        // (the admin-positive path in PersonServiceTests already
        // exercises the empty + non-empty cases; this test only
        // covers the gate refusal).
        var stubPerson = TestData.Person(Factory, "Sara", "Stub");
        TestData.Membership(Factory, stubPerson.UserId, orgA.Id, OrganizationRole.Volunteer);
        using (var db = Factory.CreateDbContext())
        {
            var p = db.People.First(p => p.UserId == stubPerson.UserId);
            p.IsStub = true;
            db.SaveChanges();
        }

        var coordResult = await svc.ListStubsAsync(orgA.Id, coordinatorId);
        var volResult = await svc.ListStubsAsync(orgA.Id, volunteerId);
        var foreignResult = await svc.ListStubsAsync(orgA.Id, otherAdminId);

        Assert.Empty(coordResult);
        Assert.Empty(volResult);
        Assert.Empty(foreignResult);
    }
}
