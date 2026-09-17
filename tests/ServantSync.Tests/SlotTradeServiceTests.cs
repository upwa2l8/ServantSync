using Microsoft.EntityFrameworkCore;
using ServantSync.Models;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Round-TRADE service tests. Matrix covers: filing authority + tradeability
/// guards, the dual approval path (serving volunteer OR coordinator-and-above),
/// decision-time re-validation, first-approver-wins staleness, and the
/// /Trades queue views.
/// </summary>
public class SlotTradeServiceTests : SqliteTestBase
{
    private SlotTradeService NewService() => new(
        Factory,
        new OrgAuthService(Factory),
        new AssignmentService(Factory, new TrainingService(Factory)));

    private record TradeFixture(
        Organization Org,
        Ministry Ministry,
        ServiceSlot Slot,
        ServiceSlot OtherSlot,
        Person Owner,
        Person Requester,
        Person Director,
        Person Admin,
        Assignment Target);

    private async Task<TradeFixture> BuildFixtureAsync()
    {
        var org = TestData.Org(Factory);
        var ministry = TestData.Ministry(Factory, org.Id);
        var slot = TestData.Slot(Factory, ministry.Id, "Sound Tech");
        var otherSlot = TestData.Slot(Factory, ministry.Id, "Vocals");
        var owner = TestData.Person(Factory, "Ollie", "Owner");
        var requester = TestData.Person(Factory, "Rita", "Requester");
        var director = TestData.Person(Factory, "Dana", "Director");
        var admin = TestData.Person(Factory, "Ada", "Admin");
        TestData.Membership(Factory, owner.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, requester.UserId, org.Id, OrganizationRole.Volunteer);
        TestData.Membership(Factory, director.UserId, org.Id, OrganizationRole.MinistryDirector);
        TestData.Membership(Factory, admin.UserId, org.Id, OrganizationRole.Admin);
        var start = DateTime.UtcNow.AddDays(7);
        var target = TestData.Assignment(Factory, owner.UserId, slot.Id, start, start.AddHours(2));
        return new(org, ministry, slot, otherSlot, owner, requester, director, admin, target);
    }

    private async Task<SlotTradeRequest> LoadRequestAsync(int id)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.SlotTradeRequests.SingleAsync(r => r.Id == id);
    }

    private async Task<Assignment?> LoadAssignmentAsync(int id)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Assignments.FirstOrDefaultAsync(a => a.Id == id);
    }

    // ─── Filing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestTrade_HappyPath_CreatesPendingRow()
    {
        var f = await BuildFixtureAsync();

        var result = await NewService().RequestTradeAsync(f.Requester.UserId, f.Target.Id);

        Assert.Equal(SlotTradeRequestResult.Succeeded, result);
        var row = await LoadRequestAsync((await OnlyRequestIdAsync()).Value);
        Assert.Equal(SlotTradeRequestStatus.Pending, row.Status);
        Assert.Equal(f.Requester.UserId, row.RequesterUserId);
        Assert.Equal(f.Owner.UserId, row.TargetOwnerUserId);
        Assert.Equal(f.Target.Id, row.TargetAssignmentId);
    }

    [Fact]
    public async Task RequestTrade_OwnerCannotRequestOwnShift()
    {
        var f = await BuildFixtureAsync();

        var result = await NewService().RequestTradeAsync(f.Owner.UserId, f.Target.Id);

        Assert.Equal(SlotTradeRequestResult.TargetNotTradeable, result);
    }

    [Fact]
    public async Task RequestTrade_PastShift_NotTradeable()
    {
        var f = await BuildFixtureAsync();
        var past = TestData.Assignment(Factory, f.Owner.UserId, f.Slot.Id,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(-7).AddHours(2));

        var result = await NewService().RequestTradeAsync(f.Requester.UserId, past.Id);

        Assert.Equal(SlotTradeRequestResult.TargetNotTradeable, result);
    }

    [Fact]
    public async Task RequestTrade_CancelledTarget_NotTradeable()
    {
        var f = await BuildFixtureAsync();
        var cancelled = TestData.Assignment(Factory, f.Owner.UserId, f.Slot.Id,
            DateTime.UtcNow.AddDays(7), DateTime.UtcNow.AddDays(7).AddHours(2),
            status: AssignmentStatus.Cancelled);

        var result = await NewService().RequestTradeAsync(f.Requester.UserId, cancelled.Id);

        Assert.Equal(SlotTradeRequestResult.TargetNotTradeable, result);
    }

    [Fact]
    public async Task RequestTrade_NonMember_PermissionDenied()
    {
        var f = await BuildFixtureAsync();
        var outsider = TestData.Person(Factory, "Out", "Sider");

        var result = await NewService().RequestTradeAsync(outsider.UserId, f.Target.Id);

        Assert.Equal(SlotTradeRequestResult.PermissionDenied, result);
    }

    [Fact]
    public async Task RequestTrade_DuplicatePendingRequest_Blocked()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        Assert.Equal(SlotTradeRequestResult.Succeeded, await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id));

        var second = await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);

        Assert.Equal(SlotTradeRequestResult.DuplicatePendingRequest, second);
    }

    [Fact]
    public async Task RequestTrade_MissingTraining_BlockedAtFiling()
    {
        var f = await BuildFixtureAsync();
        var content = TestData.TrainingContent(Factory, f.Org.Id, "Sound Cert");
        TestData.Requirement(Factory, content.Id, orgId: f.Org.Id);

        var result = await NewService().RequestTradeAsync(f.Requester.UserId, f.Target.Id);

        Assert.Equal(SlotTradeRequestResult.TrainingNotCompliant, result);
    }

    // ─── Approval authority + swap mechanics ────────────────────────────

    [Fact]
    public async Task Approve_ServingVolunteer_SwapsAssignment()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.ApproveAsync(requestId, f.Owner.UserId);

        Assert.Equal(SlotTradeDecisionResult.Succeeded, decision.Result);
        var swapped = await LoadAssignmentAsync(f.Target.Id);
        Assert.Equal(f.Requester.UserId, swapped!.PersonUserId);
        var row = await LoadRequestAsync(requestId);
        Assert.Equal(SlotTradeRequestStatus.Approved, row.Status);
        Assert.Equal(SlotTradeRequestApproverRole.ServingVolunteer, row.ApproverRole);
        Assert.Equal(f.Owner.UserId, row.DecidedByUserId);
        Assert.NotNull(row.DecidedUtc);
    }

    [Fact]
    public async Task Approve_MinistryDirector_SwapsAssignment()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.ApproveAsync(requestId, f.Director.UserId);

        Assert.Equal(SlotTradeDecisionResult.Succeeded, decision.Result);
        Assert.Equal(f.Requester.UserId, (await LoadAssignmentAsync(f.Target.Id))!.PersonUserId);
        Assert.Equal(SlotTradeRequestApproverRole.MinistryDirector, (await LoadRequestAsync(requestId)).ApproverRole);
    }

    [Fact]
    public async Task Approve_OrgAdmin_SwapsAssignment()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.ApproveAsync(requestId, f.Admin.UserId);

        Assert.Equal(SlotTradeDecisionResult.Succeeded, decision.Result);
        Assert.Equal(f.Requester.UserId, (await LoadAssignmentAsync(f.Target.Id))!.PersonUserId);
        Assert.Equal(SlotTradeRequestApproverRole.OrgAdmin, (await LoadRequestAsync(requestId)).ApproverRole);
    }

    [Fact]
    public async Task Approve_SystemAdmin_SwapsAssignment()
    {
        var f = await BuildFixtureAsync();
        var sys = TestData.Person(Factory, "Syb", "SysAdmin");
        await TestData.SeedSystemAdminRoleAsync(Factory, sys.UserId);
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.ApproveAsync(requestId, sys.UserId);

        Assert.Equal(SlotTradeDecisionResult.Succeeded, decision.Result);
        Assert.Equal(SlotTradeRequestApproverRole.SystemAdmin, (await LoadRequestAsync(requestId)).ApproverRole);
    }

    [Fact]
    public async Task Approve_PlainMemberWithoutRoles_PermissionDenied()
    {
        var f = await BuildFixtureAsync();
        var bystander = TestData.Person(Factory, "Bob", "Bystander");
        TestData.Membership(Factory, bystander.UserId, f.Org.Id, OrganizationRole.Volunteer);
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.ApproveAsync(requestId, bystander.UserId);

        Assert.Equal(SlotTradeDecisionResult.PermissionDenied, decision.Result);
        Assert.Equal(f.Owner.UserId, (await LoadAssignmentAsync(f.Target.Id))!.PersonUserId);
    }

    [Fact]
    public async Task Approve_SecondApproverGetsStale_FirstSwapIntact()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;
        Assert.Equal(SlotTradeDecisionResult.Succeeded, (await svc.ApproveAsync(requestId, f.Owner.UserId)).Result);

        var second = await svc.ApproveAsync(requestId, f.Director.UserId);

        Assert.Equal(SlotTradeDecisionResult.StaleOrAlreadyDecided, second.Result);
        Assert.Equal(f.Requester.UserId, (await LoadAssignmentAsync(f.Target.Id))!.PersonUserId);
    }

    [Fact]
    public async Task Approve_AfterTargetCancelled_Stale()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;
        // The owner cancels the shift after the request was filed.
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var a = await db.Assignments.SingleAsync(x => x.Id == f.Target.Id);
            a.Status = AssignmentStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var decision = await svc.ApproveAsync(requestId, f.Owner.UserId);

        Assert.Equal(SlotTradeDecisionResult.StaleOrAlreadyDecided, decision.Result);
    }

    [Fact]
    public async Task Approve_RequesterHasOverlappingShift_ValidationFailed_StaysPending()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;
        // Requester picks up a conflicting shift AFTER filing.
        TestData.Assignment(Factory, f.Requester.UserId, f.OtherSlot.Id,
            f.Target.StartUtc.AddMinutes(-30), f.Target.StartUtc.AddMinutes(30));

        var decision = await svc.ApproveAsync(requestId, f.Owner.UserId);

        Assert.Equal(SlotTradeDecisionResult.ValidationFailed, decision.Result);
        Assert.NotEmpty(decision.Conflicts);
        // Nothing changed: request still Pending, owner still holds the shift.
        Assert.Equal(SlotTradeRequestStatus.Pending, (await LoadRequestAsync(requestId)).Status);
        Assert.Equal(f.Owner.UserId, (await LoadAssignmentAsync(f.Target.Id))!.PersonUserId);
    }

    [Fact]
    public async Task Approve_RequesterTrainingRevokedAfterFiling_ValidationFailed()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        // No requirement at filing time → request succeeds.
        Assert.Equal(SlotTradeRequestResult.Succeeded,
            await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id));
        var requestId = (await OnlyRequestIdAsync()).Value;
        // Then the org adds a training requirement.
        var content = TestData.TrainingContent(Factory, f.Org.Id, "Late Cert");
        TestData.Requirement(Factory, content.Id, orgId: f.Org.Id);

        var decision = await svc.ApproveAsync(requestId, f.Owner.UserId);

        Assert.Equal(SlotTradeDecisionResult.ValidationFailed, decision.Result);
        Assert.Contains(decision.MissingTrainings, m => m.Contains("Late Cert"));
    }

    // ─── Decline / Cancel ───────────────────────────────────────────────

    [Fact]
    public async Task Decline_ServingVolunteer_MarksDeclined_AssignmentUntouched()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.DeclineAsync(requestId, f.Owner.UserId);

        Assert.Equal(SlotTradeDecisionResult.Succeeded, decision.Result);
        Assert.Equal(SlotTradeRequestStatus.Declined, (await LoadRequestAsync(requestId)).Status);
        Assert.Equal(f.Owner.UserId, (await LoadAssignmentAsync(f.Target.Id))!.PersonUserId);
    }

    [Fact]
    public async Task Decline_UninvolvedVolunteer_PermissionDenied()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.DeclineAsync(requestId, f.Requester.UserId);

        Assert.Equal(SlotTradeDecisionResult.PermissionDenied, decision.Result);
        Assert.Equal(SlotTradeRequestStatus.Pending, (await LoadRequestAsync(requestId)).Status);
    }

    [Fact]
    public async Task Decline_AlreadyDecided_Stale()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;
        await svc.DeclineAsync(requestId, f.Owner.UserId);

        var second = await svc.DeclineAsync(requestId, f.Admin.UserId);

        Assert.Equal(SlotTradeDecisionResult.StaleOrAlreadyDecided, second.Result);
    }

    [Fact]
    public async Task Cancel_ByRequester_MarksCancelled()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.CancelAsync(requestId, f.Requester.UserId);

        Assert.Equal(SlotTradeDecisionResult.Succeeded, decision.Result);
        Assert.Equal(SlotTradeRequestStatus.Cancelled, (await LoadRequestAsync(requestId)).Status);
    }

    [Fact]
    public async Task Cancel_BySomeoneElse_NotFound()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;

        var decision = await svc.CancelAsync(requestId, f.Admin.UserId);

        Assert.Equal(SlotTradeDecisionResult.NotFound, decision.Result);
        Assert.Equal(SlotTradeRequestStatus.Pending, (await LoadRequestAsync(requestId)).Status);
    }

    // ─── Queue views ────────────────────────────────────────────────────

    [Fact]
    public async Task ListPendingForServingVolunteer_ShowsRequestsWithApproveFlag()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id, message: "I can take this one");

        var forOwner = await svc.ListPendingForServingVolunteerAsync(f.Owner.UserId);
        var forRequester = await svc.ListPendingForServingVolunteerAsync(f.Requester.UserId);

        var view = Assert.Single(forOwner);
        Assert.True(view.ViewerCanApprove);
        Assert.Equal(f.Requester.UserId, view.RequesterUserId);
        Assert.Equal("Ollie Owner", view.TargetOwnerName);
        Assert.Equal("Rita Requester", view.RequesterName);
        Assert.Equal("I can take this one", view.Message);
        Assert.Empty(forRequester);
    }

    [Fact]
    public async Task ListPendingForApprover_DirectorSeesIt_PlainVolunteerDoesNot()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);

        var bystander = TestData.Person(Factory, "Ned", "Nobody");
        var forDirector = await svc.ListPendingForApproverAsync(f.Director.UserId);
        var forBystander = await svc.ListPendingForApproverAsync(bystander.UserId);

        Assert.True(forDirector.Single().ViewerCanApprove);
        Assert.Empty(forBystander);
    }

    [Fact]
    public async Task ListMine_ShowsOwnRequestsAcrossStatuses()
    {
        var f = await BuildFixtureAsync();
        var svc = NewService();
        await svc.RequestTradeAsync(f.Requester.UserId, f.Target.Id);
        var requestId = (await OnlyRequestIdAsync()).Value;
        await svc.DeclineAsync(requestId, f.Owner.UserId);

        var mine = await svc.ListMineAsync(f.Requester.UserId);
        var theirs = await svc.ListMineAsync(f.Owner.UserId);

        var view = Assert.Single(mine);
        Assert.Equal(SlotTradeRequestStatus.Declined, view.Status);
        Assert.True(view.ViewerIsRequester);
        Assert.False(view.ViewerCanApprove);
        Assert.Empty(theirs);
    }

    /// <summary>Every fixture files exactly one request; helper finds its id.</summary>
    private async Task<int?> OnlyRequestIdAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        var ids = await db.SlotTradeRequests.Select(r => (int?)r.Id).ToListAsync();
        return ids.Count == 1 ? ids[0] : null;
    }
}
