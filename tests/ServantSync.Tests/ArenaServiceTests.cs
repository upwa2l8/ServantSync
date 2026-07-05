using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Service-integration tests for <see cref="ArenaService"/>. The arena
/// creation path was moved out of <c>Detail.razor</c> into a service so
/// the Admin-only gate lives in one place and the contract is testable
/// without spinning up Blazor. Real SQLite-backed DbContext via the
/// shared <see cref="SqliteTestBase"/>, no mocks.
/// </summary>
public class ArenaServiceTests : SqliteTestBase
{
    private ArenaService NewSvc() => new(
        Factory,
        new OrgAuthService(Factory),
        NullLogger<ArenaService>.Instance);

    [Fact]
    public async Task Create_AdminCaller_AddsRow_AndReturnsAdded()
    {
        var org = TestData.Org(Factory);
        var admin = TestData.Person(Factory);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);

        var result = await NewSvc().CreateAsync(
            callerUserId: admin.UserId,
            organizationId: org.Id,
            name: "Field 1",
            surfaceType: "Grass",
            capacity: 22,
            isActive: true);

        Assert.Equal(ArenaAddResult.Added, result);

        await using var db = await Factory.CreateDbContextAsync();
        var row = await db.Arenas.SingleAsync(a =>
            a.OrganizationId == org.Id && a.Name == "Field 1");
        Assert.Equal("Grass", row.SurfaceType);
        Assert.Equal(22, row.Capacity);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Create_CoordinatorCaller_Denied()
    {
        // Coordinators used to be allowed (CanManageOrgAsync) under the
        // old RBAC; the new RBAC tightens arena management to Admin-only.
        // Locks in the strictness so a future refactor that returns to
        // CanManageOrgAsync would fail this test.
        var org = TestData.Org(Factory);
        var coordinator = TestData.Person(Factory);
        TestData.Membership(Factory, coordinator.UserId, org.Id, OrganizationRole.Coordinator);

        var result = await NewSvc().CreateAsync(
            callerUserId: coordinator.UserId,
            organizationId: org.Id,
            name: "Field 1",
            surfaceType: null,
            capacity: null,
            isActive: true);

        Assert.Equal(ArenaAddResult.PermissionDenied, result);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.False(await db.Arenas.AnyAsync(a => a.Name == "Field 1"));
    }

    [Fact]
    public async Task Create_VolunteerCaller_Denied()
    {
        var org = TestData.Org(Factory);
        var volunteer = TestData.Person(Factory);
        TestData.Membership(Factory, volunteer.UserId, org.Id, OrganizationRole.Volunteer);

        var result = await NewSvc().CreateAsync(
            callerUserId: volunteer.UserId,
            organizationId: org.Id,
            name: "Field 1",
            surfaceType: null,
            capacity: null,
            isActive: true);

        Assert.Equal(ArenaAddResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Create_AdminOfOtherOrg_Denied()
    {
        var orgA = TestData.Org(Factory, "Org A");
        var orgB = TestData.Org(Factory, "Org B");
        var crossOrgAdmin = TestData.Person(Factory);
        TestData.Membership(Factory, crossOrgAdmin.UserId, orgA.Id, OrganizationRole.Admin);

        var result = await NewSvc().CreateAsync(
            callerUserId: crossOrgAdmin.UserId,
            organizationId: orgB.Id,
            name: "Field 1",
            surfaceType: null,
            capacity: null,
            isActive: true);

        Assert.Equal(ArenaAddResult.PermissionDenied, result);
    }

    [Fact]
    public async Task Create_EmptyCaller_Denied()
    {
        var org = TestData.Org(Factory);

        var result = await NewSvc().CreateAsync(
            callerUserId: "",
            organizationId: org.Id,
            name: "Field 1",
            surfaceType: null,
            capacity: null,
            isActive: true);

        Assert.Equal(ArenaAddResult.PermissionDenied, result);
    }

}
