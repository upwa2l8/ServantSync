using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Round-TRADE implementation. See <see cref="ISlotTradeService"/> for the contract.
///
/// Concurrency model: every decision runs as an atomic guarded UPDATE (the
/// SET carries a WHERE on the row still being Pending / still held by the
/// expected owner), so the first writer wins and a loser is detected by
/// rows-affected == 0 (same stamp-guard pattern as
/// AssignmentService.UpdateAssignmentStatusAsync). On approval the claim and
/// the Assignment re-parent run back-to-back inside the implicit
/// transaction SQLite gives each ExecuteUpdate/SaveChanges, so the trade
/// row and the schedule can never disagree: there is no intermediate
/// "approved but not swapped" state.
/// </summary>
public sealed class SlotTradeService : ISlotTradeService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly IAssignmentService _assignments;

    // Email notification collaborators. Nullable so unit tests can build
    // the service without SMTP plumbing; DI always supplies all three.
    private readonly ILogger<SlotTradeService>? _log;
    private readonly IEmailBrandAssets? _brand;
    private readonly EmailOptions? _emailOpts;

    public SlotTradeService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        IAssignmentService assignments,
        ILogger<SlotTradeService>? log = null,
        IEmailBrandAssets? brand = null,
        IOptions<EmailOptions>? emailOpts = null)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _assignments = assignments;
        _log = log;
        _brand = brand;
        _emailOpts = emailOpts?.Value;
    }

    // ------------------------------------------------------------------
    // Request
    // ------------------------------------------------------------------

    public async Task<SlotTradeRequestResult> RequestTradeAsync(
        string userId,
        int targetAssignmentId,
        string? onBehalfOfUserId = null,
        string? message = null,
        CancellationToken ct = default)
    {
        var effectiveRequester = string.IsNullOrWhiteSpace(onBehalfOfUserId) ? userId : onBehalfOfUserId;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var target = await db.Assignments
            .Include(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == targetAssignmentId, ct);
        if (target is null) return SlotTradeRequestResult.AssignmentNotFound;

        var orgId = target.ServiceSlot.Ministry.OrganizationId;

        // Authority: the caller must be a member of the owning org. Filing on
        // behalf of someone else additionally requires org-admin or
        // system-admin (mirrors TrainingSessionService.SignUpAsync's
        // userId/onBehalfOfUserId split).
        var callerRole = await _orgAuth.GetRoleAsync(userId, orgId, ct);
        var isSystemAdmin = await _orgAuth.IsSystemAdminAsync(userId, ct);
        if (callerRole is null && !isSystemAdmin)
            return SlotTradeRequestResult.PermissionDenied;
        if (effectiveRequester != userId
            && callerRole != OrganizationRole.Admin
            && !isSystemAdmin)
        {
            return SlotTradeRequestResult.PermissionDenied;
        }

        // Tradeability: Scheduled + future + active slot + not already yours.
        if (target.Status != AssignmentStatus.Scheduled
            || target.StartUtc <= DateTime.UtcNow
            || !target.ServiceSlot.IsActive
            || target.PersonUserId == effectiveRequester)
        {
            return SlotTradeRequestResult.TargetNotTradeable;
        }

        // One Pending request per (requester, shift).
        var duplicate = await db.SlotTradeRequests.AnyAsync(r =>
            r.Status == SlotTradeRequestStatus.Pending
            && r.RequesterUserId == effectiveRequester
            && r.TargetAssignmentId == targetAssignmentId, ct);
        if (duplicate) return SlotTradeRequestResult.DuplicatePendingRequest;

        // Training compliance up front: a coordinator discovering a
        // disqualification at approval time is a bad experience. Surface it
        // at filing time; approval re-validates anyway (defense in depth).
        // excludeAssignmentId = the target: a trade TAKES OVER the owner's
        // seat, so capacity never increases and the owner's own row must not
        // count as "shift is full" (nor as a self-conflict for the owner
        // filing on their own shift). The other-volunteer overlap conflicts
        // the requester WOULD hit still fire normally.
        var validation = await _assignments.ValidateAsync(
            effectiveRequester,
            target.ServiceSlotId,
            target.StartUtc,
            target.EndUtc,
            excludeAssignmentId: target.Id,
            ct);
        if (validation.MissingTrainings.Count > 0)
            return SlotTradeRequestResult.TrainingNotCompliant;

        db.SlotTradeRequests.Add(new SlotTradeRequest
        {
            RequesterUserId = effectiveRequester,
            TargetAssignmentId = targetAssignmentId,
            TargetOwnerUserId = target.PersonUserId,
            RequestedForUserId = effectiveRequester,
            Status = SlotTradeRequestStatus.Pending,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        await NotifyApproversOfNewRequestAsync(db, target, effectiveRequester, ct);

        return SlotTradeRequestResult.Succeeded;
    }

    // ------------------------------------------------------------------
    // Approve / Decline / Cancel
    // ------------------------------------------------------------------

    public async Task<SlotTradeDecision> ApproveAsync(int requestId, string approverUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var request = await db.SlotTradeRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return SlotTradeDecision.Fail(SlotTradeDecisionResult.NotFound);

        var target = await db.Assignments
            .Include(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .FirstOrDefaultAsync(a => a.Id == request.TargetAssignmentId, ct);
        if (target is null) return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);

        var orgId = target.ServiceSlot.Ministry.OrganizationId;
        var role = await _orgAuth.GetRoleAsync(approverUserId, orgId, ct);
        var approverRole = await ClassifyApproverAsync(approverUserId, request, role, ct);
        if (approverRole is null)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.PermissionDenied);

        // Re-validate at decision time: anything may have changed since the
        // request was filed (assignment cancelled, shift started, ownership
        // changed via another approved trade).
        if (request.Status != SlotTradeRequestStatus.Pending)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);
        if (target.Status != AssignmentStatus.Scheduled
            || target.StartUtc <= DateTime.UtcNow
            || target.PersonUserId != request.TargetOwnerUserId)
        {
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);
        }

        // Same exclude rationale as filing: the swap consumes the owner's
        // seat rather than adding one, so the owner's row must not trip the
        // capacity gate. Real overlaps with the requester's OTHER shifts and
        // training gaps still block the approval.
        var validation = await _assignments.ValidateAsync(
            request.RequesterUserId,
            target.ServiceSlotId,
            target.StartUtc,
            target.EndUtc,
            excludeAssignmentId: request.TargetAssignmentId,
            ct);
        if (!validation.Succeeded)
        {
            return SlotTradeDecision.Fail(
                SlotTradeDecisionResult.ValidationFailed,
                validation.Conflicts,
                validation.MissingTrainings);
        }

        // Atomic claim: the status flip is guarded on BOTH the request still
        // being Pending AND the target assignment still being held by the
        // person it was held by at request time. If another approver got
        // here first, the UPDATE matches 0 rows and we abort without
        // writing the Assignment (first-approver-wins).
        var claimed = await db.SlotTradeRequests
            .Where(r => r.Id == requestId
                && r.Status == SlotTradeRequestStatus.Pending
                && r.TargetAssignment.PersonUserId == r.TargetOwnerUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SlotTradeRequestStatus.Approved)
                .SetProperty(r => r.DecidedUtc, DateTime.UtcNow)
                .SetProperty(r => r.DecidedByUserId, approverUserId)
                .SetProperty(r => r.ApproverRole, approverRole), ct);
        if (claimed == 0)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);

        // The swap. The claim's WHERE already pinned the owner, so this
        // UPDATE can only fail in a pathological race; we still guard it so
        // a 0-row write surfaces as a stale decision rather than a silent
        // no-op with a stamped request.
        var swapped = await db.Assignments
            .Where(a => a.Id == request.TargetAssignmentId
                && a.PersonUserId == request.TargetOwnerUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.PersonUserId, request.RequesterUserId), ct);
        if (swapped == 0)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);

        await NotifyRequesterOfOutcomeAsync(db, requestId, approved: true, ct);

        return SlotTradeDecision.Ok(new Assignment
        {
            Id = request.TargetAssignmentId,
            ServiceSlotId = target.ServiceSlotId,
            StartUtc = target.StartUtc,
            EndUtc = target.EndUtc,
            PersonUserId = request.RequesterUserId,
            Status = target.Status,
        });
    }

    public async Task<SlotTradeDecision> DeclineAsync(int requestId, string approverUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var request = await db.SlotTradeRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return SlotTradeDecision.Fail(SlotTradeDecisionResult.NotFound);

        var target = await db.Assignments
            .Include(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.TargetAssignmentId, ct);
        if (target is null) return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);

        var orgId = target.ServiceSlot.Ministry.OrganizationId;
        var role = await _orgAuth.GetRoleAsync(approverUserId, orgId, ct);
        var approverRole = await ClassifyApproverAsync(approverUserId, request, role, ct);
        if (approverRole is null)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.PermissionDenied);

        if (request.Status != SlotTradeRequestStatus.Pending)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);

        var stampGuard = await db.SlotTradeRequests
            .Where(r => r.Id == requestId && r.Status == SlotTradeRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SlotTradeRequestStatus.Declined)
                .SetProperty(r => r.DecidedUtc, DateTime.UtcNow)
                .SetProperty(r => r.DecidedByUserId, approverUserId)
                .SetProperty(r => r.ApproverRole, approverRole), ct);
        if (stampGuard == 0)
            return SlotTradeDecision.Fail(SlotTradeDecisionResult.StaleOrAlreadyDecided);

        await NotifyRequesterOfOutcomeAsync(db, requestId, approved: false, ct);

        return SlotTradeDecision.Ok(null);
    }

    public async Task<SlotTradeDecision> CancelAsync(int requestId, string requesterUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Stamp-guarded on Pending AND requester: another actor's decision
        // landing first turns this into a 0-row update.
        var stampGuard = await db.SlotTradeRequests
            .Where(r => r.Id == requestId
                && r.Status == SlotTradeRequestStatus.Pending
                && r.RequesterUserId == requesterUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SlotTradeRequestStatus.Cancelled)
                .SetProperty(r => r.DecidedUtc, DateTime.UtcNow)
                .SetProperty(r => r.DecidedByUserId, requesterUserId), ct);
        if (stampGuard == 0) return SlotTradeDecision.Fail(SlotTradeDecisionResult.NotFound);

        return SlotTradeDecision.Ok(null);
    }

    // ------------------------------------------------------------------
    // Notifications (best-effort, never blocking the decision)
    // ------------------------------------------------------------------

    /// <summary>
    /// New requests email the serving volunteer + MinistryDirectors + Admins
    /// of the owning org (Round-TRADE: a request nobody knows about can sit
    /// forever). Decisions email the requester. Fire-and-forget: SMTP config
    /// errors are logged, never surfaced — a notification failure must not
    /// fail the request/approval (same contract as FeatureRequestService's
    /// notification path). Recipients resolve via Identity at send time;
    /// placeholder stubs with unclaimed mailboxes are skipped.
    /// </summary>
    private async Task NotifyApproversOfNewRequestAsync(
        ApplicationDbContext db,
        Assignment target,
        string requesterUserId,
        CancellationToken ct)
    {
        try
        {
            if (_emailOpts is null || _brand is null) return;
            if (string.IsNullOrWhiteSpace(_emailOpts.Smtp.Host)) return;

            var slot = target.ServiceSlot;
            var orgId = slot.Ministry.OrganizationId;

            var approverUserIds = new List<string> { target.PersonUserId };
            approverUserIds.AddRange(await db.OrganizationMemberships
                .Where(m => m.OrganizationId == orgId
                    && (m.Role == OrganizationRole.MinistryDirector || m.Role == OrganizationRole.Admin))
                .Select(m => m.PersonUserId)
                .ToListAsync(ct));

            var users = await db.Users
                .Where(u => approverUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

            var requester = await db.People
                .Where(p => p.UserId == requesterUserId)
                .Select(p => p.DisplayName)
                .FirstOrDefaultAsync(ct);

            var when = $"{target.StartUtc:dddd, MMM d} · {target.StartUtc:h:mm tt}";
            var skipEmail = (string? e) => string.IsNullOrWhiteSpace(e)
                || e.Contains("@test.local")
                || e.EndsWith("@example.com");

            var messages = new List<(string To, string Subject, string InnerHtml)>();
            foreach (var userId in approverUserIds.Distinct())
            {
                if (!users.TryGetValue(userId, out var email) || skipEmail(email)) continue;
                messages.Add((email!,
                    $"Trade request: {slot.Name} on {when}",
                    $"<p><strong>{System.Net.WebUtility.HtmlEncode(requester ?? "A volunteer")}</strong> " +
                    $"would like to take your spot — or has asked the coordinators to arrange it.</p>" +
                    $"<p><strong>{System.Net.WebUtility.HtmlEncode(slot.Name)}</strong><br/>" +
                    $"{System.Net.WebUtility.HtmlEncode(slot.Ministry.Name)}<br/>{when}</p>" +
                    $"<p><a class=\"btn\" href=\"/Trades\">Review trade requests</a></p>"));
            }
            if (messages.Count == 0) return;
            await SendTradeEmailsAsync(messages, ct);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Trade request notification failed for assignment {AssignmentId}.", target.Id);
        }
    }

    private async Task NotifyRequesterOfOutcomeAsync(
        ApplicationDbContext db,
        int requestId,
        bool approved,
        CancellationToken ct)
    {
        try
        {
            if (_emailOpts is null || _brand is null) return;
            if (string.IsNullOrWhiteSpace(_emailOpts.Smtp.Host)) return;

            var row = await db.SlotTradeRequests
                .Include(r => r.TargetAssignment).ThenInclude(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == requestId, ct);
            if (row is null) return;

            var requesterUser = await db.Users.FirstOrDefaultAsync(u => u.Id == row.RequesterUserId, ct);
            var requesterEmail = requesterUser?.Email;
            if (string.IsNullOrWhiteSpace(requesterEmail)
                || requesterEmail!.Contains("@test.local")
                || requesterEmail.EndsWith("@example.com"))
            {
                return; // stub / seeded account — nowhere to send
            }

            var slot = row.TargetAssignment.ServiceSlot;
            var when = $"{row.TargetAssignment.StartUtc:dddd, MMM d} · {row.TargetAssignment.StartUtc:h:mm tt}";
            var subject = approved
                ? $"Trade approved: {slot.Name} on {when}"
                : $"Trade declined: {slot.Name} on {when}";
            var innerHtml = approved
                ? $"<p>Good news — your trade request was approved.</p>" +
                  $"<p><strong>{System.Net.WebUtility.HtmlEncode(slot.Name)}</strong><br/>" +
                  $"{System.Net.WebUtility.HtmlEncode(slot.Ministry.Name)}<br/>{when}</p>" +
                  $"<p>The shift is now on your <a href=\"/MySchedule\">My schedule</a>.</p>"
                : $"<p>Your trade request was declined.</p>" +
                  $"<p><strong>{System.Net.WebUtility.HtmlEncode(slot.Name)}</strong><br/>" +
                  $"{System.Net.WebUtility.HtmlEncode(slot.Ministry.Name)}<br/>{when}</p>";

            await SendTradeEmailsAsync(new List<(string, string, string)> { (requesterEmail!, subject, innerHtml) }, ct);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Trade decision notification failed for request {RequestId}.", requestId);
        }
    }

    /// <summary>One SMTP connection for the whole batch (mirror of FeatureRequestService's dispatch loop, generalized to N messages).</summary>
    private async Task SendTradeEmailsAsync(List<(string To, string Subject, string InnerHtml)> messages, CancellationToken ct)
    {
        var opts = _emailOpts!;
        using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
        var tlsMode = (opts.Smtp.TlsMode ?? "").Trim().ToLowerInvariant() switch
        {
            "none" => MailKit.Security.SecureSocketOptions.None,
            "ssl" or "sslconnect" or "ssl_on_connect" => MailKit.Security.SecureSocketOptions.SslOnConnect,
            "starttls" => MailKit.Security.SecureSocketOptions.StartTls,
            _ => MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable,
        };
        await smtpClient.ConnectAsync(opts.Smtp.Host, opts.Smtp.Port, tlsMode, ct);
        if (!string.IsNullOrEmpty(opts.Smtp.User))
            await smtpClient.AuthenticateAsync(opts.Smtp.User, opts.Smtp.Password ?? "", ct);
        foreach (var (to, subject, innerHtml) in messages)
        {
            var message = MailKitEmailSender.BuildMessage(opts, _brand!, to, subject, innerHtml);
            await smtpClient.SendAsync(message, ct);
        }
        await smtpClient.DisconnectAsync(quit: true, ct);
    }

    /// <summary>
    /// Approval authority: serving volunteer OR MinistryDirector OR Admin of
    /// the owning org OR SystemAdmin. Returns the role to stamp on the row,
    /// or null when the caller has no authority.
    /// </summary>
    private async Task<SlotTradeRequestApproverRole?> ClassifyApproverAsync(
        string approverUserId,
        SlotTradeRequest request,
        OrganizationRole? roleInOwningOrg,
        CancellationToken ct)
    {
        if (approverUserId == request.TargetOwnerUserId)
            return SlotTradeRequestApproverRole.ServingVolunteer;
        if (roleInOwningOrg == OrganizationRole.MinistryDirector)
            return SlotTradeRequestApproverRole.MinistryDirector;
        if (roleInOwningOrg == OrganizationRole.Admin)
            return SlotTradeRequestApproverRole.OrgAdmin;
        if (await _orgAuth.IsSystemAdminAsync(approverUserId, ct))
            return SlotTradeRequestApproverRole.SystemAdmin;
        return null;
    }

    // ------------------------------------------------------------------
    // List views
    // ------------------------------------------------------------------

    public async Task<List<SlotTradeRequestView>> ListPendingForApproverAsync(string viewerUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.SlotTradeRequests
            .Where(r => r.Status == SlotTradeRequestStatus.Pending)
            .Include(r => r.TargetAssignment).ThenInclude(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .AsNoTracking()
            .ToListAsync(ct);
        // "For approver" means it: only rows where the viewer is actually a
        // listed approver (serving volunteer or coordinator-and-above of the
        // owning org). Everything else would be noise on the /Trades queue.
        var views = await ToViewsAsync(db, rows, viewerUserId, ct);
        return views.Where(v => v.ViewerCanApprove).ToList();
    }

    public async Task<List<SlotTradeRequestView>> ListPendingForServingVolunteerAsync(string viewerUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.SlotTradeRequests
            .Where(r => r.Status == SlotTradeRequestStatus.Pending
                && r.TargetOwnerUserId == viewerUserId)
            .Include(r => r.TargetAssignment).ThenInclude(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .AsNoTracking()
            .ToListAsync(ct);
        return await ToViewsAsync(db, rows, viewerUserId, ct);
    }

    public async Task<List<SlotTradeRequestView>> ListMineAsync(string viewerUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.SlotTradeRequests
            .Where(r => r.RequesterUserId == viewerUserId)
            .Include(r => r.TargetAssignment).ThenInclude(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .OrderByDescending(r => r.CreatedUtc)
            .AsNoTracking()
            .ToListAsync(ct);
        return await ToViewsAsync(db, rows, viewerUserId, ct);
    }

    /// <summary>
    /// Shared materializer: batches the viewer's per-org role lookups and
    /// name lookups, then maps rows to <see cref="SlotTradeRequestView"/>s.
    /// </summary>
    private async Task<List<SlotTradeRequestView>> ToViewsAsync(
        ApplicationDbContext db,
        List<SlotTradeRequest> rows,
        string viewerUserId,
        CancellationToken ct)
    {
        if (rows.Count == 0) return new();

        var isSystemAdmin = await _orgAuth.IsSystemAdminAsync(viewerUserId, ct);

        // Distinct orgs whose role-for-viewer the approval gate needs.
        var orgIds = rows
            .Select(r => r.TargetAssignment.ServiceSlot.Ministry.OrganizationId)
            .Distinct()
            .ToList();
        var roleByOrg = new Dictionary<int, OrganizationRole?>();
        foreach (var orgId in orgIds)
        {
            roleByOrg[orgId] = await _orgAuth.GetRoleAsync(viewerUserId, orgId, ct);
        }

        var requesterNames = await db.People
            .Where(p => rows.Select(r => r.RequesterUserId).Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);
        var ownerNames = await db.People
            .Where(p => rows.Select(r => r.TargetOwnerUserId).Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);

        return rows
            .Select(r =>
            {
                var slot = r.TargetAssignment.ServiceSlot;
                var ministry = slot.Ministry;
                var role = roleByOrg.TryGetValue(ministry.OrganizationId, out var v) ? v : null;
                return new SlotTradeRequestView(
                    RequestId: r.Id,
                    Status: r.Status,
                    RequesterUserId: r.RequesterUserId,
                    RequesterName: requesterNames.TryGetValue(r.RequesterUserId, out var rn) ? rn : r.RequesterUserId,
                    TargetOwnerName: ownerNames.TryGetValue(r.TargetOwnerUserId, out var on) ? on : r.TargetOwnerUserId,
                    TargetAssignmentId: r.TargetAssignmentId,
                    ServiceSlotId: slot.Id,
                    SlotName: slot.Name,
                    MinistryName: ministry.Name,
                    OrganizationId: ministry.OrganizationId,
                    StartUtc: r.TargetAssignment.StartUtc,
                    EndUtc: r.TargetAssignment.EndUtc,
                    Location: slot.Location,
                    ViewerCanApprove: r.Status == SlotTradeRequestStatus.Pending && (
                        viewerUserId == r.TargetOwnerUserId
                        || role == OrganizationRole.MinistryDirector
                        || role == OrganizationRole.Admin
                        || isSystemAdmin),
                    ViewerIsRequester: r.RequesterUserId == viewerUserId,
                    Message: r.Message,
                    CreatedUtc: r.CreatedUtc);
            })
            .OrderBy(v => v.StartUtc)
            .ToList();
    }
}
