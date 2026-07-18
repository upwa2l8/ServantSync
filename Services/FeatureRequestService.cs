using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class FeatureRequestService : IFeatureRequestService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly ILogger<FeatureRequestService> _log;
    private readonly IEmailBrandAssets _brand;
    private readonly EmailOptions _emailOpts;

    /// <summary>Max feature requests per IP per hour (anti-spam).</summary>
    private const int MaxRequestsPerHour = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    public FeatureRequestService(
        IDbContextFactory<ApplicationDbContext> factory,
        ILogger<FeatureRequestService> log,
        IEmailBrandAssets brand,
        IOptions<EmailOptions> emailOpts)
    {
        _factory = factory;
        _log = log;
        _brand = brand;
        _emailOpts = emailOpts.Value;
    }

    public async Task<(FeatureRequestSubmitResult Result, string? Message)> SubmitAsync(
        string title,
        string description,
        string? submitterName,
        string? submitterEmail,
        string? submitterUserId,
        string? submitterIp,
        string? honeypot)
    {
        // Layer 1: honeypot check.
        if (!string.IsNullOrEmpty(honeypot))
        {
            _log.LogWarning("FeatureRequest honeypot triggered from IP {Ip}.", submitterIp);
            return (FeatureRequestSubmitResult.HoneypotFailed, null);
        }

        // Layer 2: IP rate limit (3 per hour).
        if (!string.IsNullOrEmpty(submitterIp))
        {
            await using var checkDb = await _factory.CreateDbContextAsync();
            var cutoff = DateTime.UtcNow - RateLimitWindow;
            var recentCount = await checkDb.FeatureRequests
                .CountAsync(f => f.SubmitterIp == submitterIp && f.CreatedUtc >= cutoff);
            if (recentCount >= MaxRequestsPerHour)
            {
                _log.LogWarning("FeatureRequest rate-limited for IP {Ip} ({Count} in last hour).",
                    submitterIp, recentCount);
                return (FeatureRequestSubmitResult.RateLimited,
                    "You've submitted too many requests recently. Please try again later.");
            }
        }

        // Validation.
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
        {
            return (FeatureRequestSubmitResult.ValidationError, "Title and description are required.");
        }

        await using var db = await _factory.CreateDbContextAsync();
        db.FeatureRequests.Add(new FeatureRequest
        {
            Title = title.Trim(),
            Description = description.Trim(),
            SubmitterName = string.IsNullOrWhiteSpace(submitterName) ? null : submitterName.Trim(),
            SubmitterEmail = string.IsNullOrWhiteSpace(submitterEmail) ? null : submitterEmail.Trim(),
            SubmitterUserId = submitterUserId,
            SubmitterIp = submitterIp,
            Status = FeatureRequestStatus.New,
        });
        await db.SaveChangesAsync();

        _log.LogInformation("FeatureRequest submitted: '{Title}' from IP {Ip}.", title.Trim(), submitterIp);

        // Fire-and-forget email notification to all SystemAdmins.
        _ = NotifySystemAdminsAsync(title.Trim(), description.Trim(), submitterName, submitterIp);

        return (FeatureRequestSubmitResult.Success, null);
    }

    public async Task<IReadOnlyList<FeatureRequestRow>> ListAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FeatureRequests
            .OrderByDescending(f => f.CreatedUtc)
            .Select(f => new FeatureRequestRow
            {
                Id = f.Id,
                Title = f.Title,
                Description = f.Description,
                SubmitterName = f.SubmitterName,
                SubmitterEmail = f.SubmitterEmail,
                SubmitterIp = f.SubmitterIp,
                Status = f.Status,
                TriageNotes = f.TriageNotes,
                LinkedSpec = f.LinkedSpec,
                CreatedUtc = f.CreatedUtc,
                TriagedUtc = f.TriagedUtc,
            })
            .ToListAsync(ct);
    }

    public async Task<bool> TriageAsync(
        int featureRequestId,
        FeatureRequestStatus status,
        string? triageNotes,
        string? linkedSpec,
        string triagedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var fr = await db.FeatureRequests.FirstOrDefaultAsync(f => f.Id == featureRequestId, ct);
        if (fr is null) return false;

        fr.Status = status;
        fr.TriageNotes = string.IsNullOrWhiteSpace(triageNotes) ? null : triageNotes.Trim();
        fr.LinkedSpec = string.IsNullOrWhiteSpace(linkedSpec) ? null : linkedSpec.Trim();
        fr.TriagedUtc = DateTime.UtcNow;
        fr.TriagedByUserId = triagedByUserId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Notify all SystemAdmins about a new feature request via branded email.
    /// Fire-and-forget — failures are logged but never surface to the submitter.
    /// </summary>
    private async Task NotifySystemAdminsAsync(
        string title, string description, string? submitterName, string? submitterIp)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync();

            // Resolve the SystemAdmin role id.
            var systemAdminRoleId = await db.Roles
                .Where(r => r.NormalizedName == "SYSTEMADMIN")
                .Select(r => r.Id)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(systemAdminRoleId)) return;

            // Get all SystemAdmin user emails.
            var adminEmails = await db.UserRoles
                .Where(ur => ur.RoleId == systemAdminRoleId)
                .Join(db.Users, ur => ur.UserId, u => u.Id, (ur, u) => u)
                .Where(u => u.Email != null)
                .Select(u => u.Email!)
                .ToListAsync();
            if (adminEmails.Count == 0) return;

            var submitterLine = !string.IsNullOrEmpty(submitterName)
                ? $"Submitter: {System.Net.WebUtility.HtmlEncode(submitterName)}"
                : (!string.IsNullOrEmpty(submitterIp)
                    ? $"Submitted from IP: {System.Net.WebUtility.HtmlEncode(submitterIp)}"
                    : "Submitted anonymously");

            var innerHtml =
                $"<p>A new feature request was submitted on ServantSync:</p>" +
                $"<p><strong>{System.Net.WebUtility.HtmlEncode(title)}</strong></p>" +
                $"<p style=\"white-space:pre-wrap\">{System.Net.WebUtility.HtmlEncode(description)}</p>" +
                $"<p style=\"font-size:13px;color:#6b6b6b\">{submitterLine}</p>" +
                $"<p><a class=\"btn\" href=\"/SystemAdmin/FeatureRequests\">Review in triage queue</a></p>";

            using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
            var tlsMode = (_emailOpts.Smtp.TlsMode ?? "").Trim().ToLowerInvariant() switch
            {
                "none" => MailKit.Security.SecureSocketOptions.None,
                "ssl" or "sslconnect" or "ssl_on_connect" => MailKit.Security.SecureSocketOptions.SslOnConnect,
                "starttls" => MailKit.Security.SecureSocketOptions.StartTls,
                _ => MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable,
            };
            await smtpClient.ConnectAsync(_emailOpts.Smtp.Host, _emailOpts.Smtp.Port, tlsMode);
            if (!string.IsNullOrEmpty(_emailOpts.Smtp.User))
                await smtpClient.AuthenticateAsync(_emailOpts.Smtp.User, _emailOpts.Smtp.Password ?? "");

            foreach (var email in adminEmails)
            {
                var message = MailKitEmailSender.BuildMessage(
                    _emailOpts, _brand, email,
                    $"New feature request: {title}", innerHtml);
                await smtpClient.SendAsync(message);
            }

            await smtpClient.DisconnectAsync(quit: true);

            _log.LogInformation("FeatureRequest notification sent to {Count} SystemAdmin(s) for '{Title}'.", adminEmails.Count, title);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send FeatureRequest notification for '{Title}'.", title);
        }
    }
}
