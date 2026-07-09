using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="OrganizationMinistryService"/>.
/// The ministries Save path was moved out of
/// <c>Components/Pages/Ministries/Edit.razor</c> into a service so the
/// Admin-only gate lives in one place. Locks in the RBAC tightening:
/// ministry management is Admin-only now (was Admin+Coordinator before).
/// Real SQLite-backed DbContext via the shared <see cref="SqliteTestBase"/>.
/// </summary>
public class OrganizationMinistryServiceTests : SqliteTestBase
{
    private OrganizationMinistryService NewSvc() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<OrganizationMinistryService>.Instance);

    [Fact]
    public async Task UpsertNew_AdminCaller_CreatesMinistry()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: null,
            name: "Worship Team",
            description: "Music + vocals",
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.Saved, result);

        await using var db = await Factory.CreateDbContextAsync();
        var min = await db.Ministries.SingleAsync(m =>
            m.OrganizationId == org.Id && m.Name == "Worship Team");
        Assert.Equal("Music + vocals", min.Description);
    }

    [Fact]
    public async Task UpsertNew_CoordinatorCaller_Denied()
    {
        // Coordinator used to be allowed (CanManageOrgAsync) on the old
        // gate; the new gate is IsOrgAdminAsync. Locks in the strictness.
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.MinistryDirector);

        var result = await NewSvc().UpsertAsync(
            callerUserId: coordinator.UserId,
            organizationId: org.Id,
            ministryId: null,
            name: "Worship Team",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Ministries.AnyAsync(m => m.Name == "Worship Team"));
    }

    [Fact]
    public async Task UpsertNew_VolunteerCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().UpsertAsync(
            callerUserId: volunteer.UserId,
            organizationId: org.Id,
            ministryId: null,
            name: "Worship Team",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, result);
    }

    [Fact]
    public async Task UpsertNew_AdminOfOtherOrg_Denied()
    {
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var crossOrgAdmin = TestData.Person(Factory);
        TestData.Membership(Factory, crossOrgAdmin.UserId, orgA.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: crossOrgAdmin.UserId,
            organizationId: orgB.Id,
            ministryId: null,
            name: "Cross-org insert",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Ministries.AnyAsync(m => m.Name == "Cross-org insert"));
    }

    [Fact]
    public async Task UpsertNew_EmptyCaller_Denied()
    {
        var org = TestData.Org(Factory);

        var result = await NewSvc().UpsertAsync(
            callerUserId: "",
            organizationId: org.Id,
            ministryId: null,
            name: "X",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, result);
    }

    [Fact]
    public async Task UpsertNew_EmptyName_Denied()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: null,
            name: "",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, result);
    }

    [Fact]
    public async Task UpsertNew_WhitespaceName_Denied()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: null,
            name: "   ",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.PermissionDenied, result);
    }

    [Fact]
    public async Task UpsertEdit_AdminCaller_UpdatesRow()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var ministry = TestData.Ministry(Factory, org.Id, "Old Name");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: ministry.Id,
            name: "New Name",
            description: "Updated description",
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.Saved, result);

        await using var db = await Factory.CreateDbContextAsync();
        var min = await db.Ministries.FindAsync(ministry.Id);
        Assert.NotNull(min);
        Assert.Equal("New Name", min!.Name);
        Assert.Equal("Updated description", min.Description);
    }

    [Fact]
    public async Task UpsertEdit_CrossOrgMinistry_ReturnsNotFound()
    {
        // An Admin of Org A trying to edit a ministry that belongs to
        // Org B would otherwise get a confusing success/failure. The
        // scope-check returns NotFound so the page handler can show
        // "Ministry not found" rather than letting the wrong-org id
        // become a silent write.
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var adminA = TestData.Person(Factory);
        var ministryInB = TestData.Ministry(Factory, orgB.Id, "B Ministry");
        TestData.Membership(Factory, adminA.UserId, orgA.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: adminA.UserId,
            organizationId: orgA.Id,
            ministryId: ministryInB.Id,
            name: "Hijacked",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.NotFound, result);

        await using var db = await Factory.CreateDbContextAsync();
        var untouched = await db.Ministries.FindAsync(ministryInB.Id);
        Assert.Equal("B Ministry", untouched!.Name);
    }

    [Fact]
    public async Task UpsertEdit_NonExistentMinistry_ReturnsNotFound()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: 99_999,
            name: "Ghost Ministry",
            description: null,
            coordinatorPersonUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.NotFound, result);
    }

    // ----- R12 — coordinator = "" must persist as NULL -----
    //
    // Ministries/Edit.razor line 28 binds the coordinator dropdown to a
    // `string?` property via <option value="">— none —</option>. The
    // browser posts the literal empty string (never null) so writing
    // "" through to EF would otherwise trip the
    // FK_Ministries_People_CoordinatorPersonUserId constraint (SQLite
    // Error 19 observed in production). These tests pin the service's
    // normalize-at-the-chokepoint behavior.
    [Fact]
    public async Task UpsertNew_EmptyCoordinatorUserId_NullStored()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: null,
            name: "Empty Coord",
            description: null,
            coordinatorPersonUserId: "", // <-- the form's "none" affordance
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.Saved, result);

        await using var db = await Factory.CreateDbContextAsync();
        var min = await db.Ministries.SingleAsync(m => m.Name == "Empty Coord");
        Assert.Null(min.CoordinatorPersonUserId);
    }

    [Fact]
    public async Task UpsertEdit_HadCoordinator_SetToEmpty_NullStored()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var coordinator = TestData.Person(Factory);
        var ministry = TestData.Ministry(Factory, org.Id, "Already Set");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        // Seed an existing coordinator assignment so the edit path
        // has a known pre-existing value to overwrite in the act step.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            ministry.CoordinatorPersonUserId = coordinator.UserId;
            db.Ministries.Update(ministry);
            await db.SaveChangesAsync();
        }

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: ministry.Id,
            name: ministry.Name,
            description: null,
            coordinatorPersonUserId: "",  // <-- clearing via "none"
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.Saved, result);

        await using var verifyDb = await Factory.CreateDbContextAsync();
        var after = await verifyDb.Ministries.FindAsync(ministry.Id);
        Assert.Null(after!.CoordinatorPersonUserId);
    }

    [Fact]
    public async Task UpsertEdit_WhitespaceCoordinatorUserId_NullStored()
    {
        // Defensive: IsNullOrWhiteSpace covers a hand-crafted caller
        // sending "   " rather than "". The page wouldn't produce this
        // but the service is the chokepoint for ALL callers, so the
        // contract has to be wider than the page's exact posts.
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        var ministry = TestData.Ministry(Factory, org.Id, "WS test");
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().UpsertAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            ministryId: ministry.Id,
            name: ministry.Name,
            description: null,
            coordinatorPersonUserId: "   ",
            coordinatorEmail: null,
            coordinatorPhone: null);

        Assert.Equal(MinistryUpsertResult.Saved, result);

        await using var db = await Factory.CreateDbContextAsync();
        var after = await db.Ministries.FindAsync(ministry.Id);
        Assert.Null(after!.CoordinatorPersonUserId);
    }
}
