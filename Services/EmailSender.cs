using Microsoft.AspNetCore.Identity;

namespace ServantSync.Services;

/// <summary>
/// Development fallback email sender. Logs every confirmation, password-reset,
/// and 2FA message to <see cref="ILogger"/> so dev environments can copy-paste
/// the link out of the test log (instead of running an SMTP catcher like
/// smtp4dev). For production, swap for a MailKit- or SendGrid-backed
/// implementation that respects the same <see cref="IEmailSender{TUser}"/>
/// interface — only the DI registration needs to change.
///
/// Note: <see cref="SendPasswordResetCodeAsync"/> is what Identity calls when
/// the configuration option <c>opts.Tokens.PasswordResetTokenProvider</c>
/// resolves to <c>DefaultPasswordResetCodeProvider</c> instead of the default
/// link provider; we log a short code in that case, while
/// <see cref="SendPasswordResetLinkAsync"/> logs a clickable callback URL.
/// </summary>
public class LoggingEmailSender : IEmailSender<IdentityUser>
{
    private readonly ILogger<LoggingEmailSender> _log;

    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
    {
        _log.LogInformation("[EMAIL] confirm-account → {Email}: {Link}", email, confirmationLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink)
    {
        _log.LogInformation("[EMAIL] password-reset-link → {Email}: {Link}", email, resetLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
    {
        _log.LogInformation("[EMAIL] password-reset-code → {Email}: {Code}", email, resetCode);
        return Task.CompletedTask;
    }

    public Task SendTwoFactorCodeAsync(IdentityUser user, string email, string code)
    {
        _log.LogInformation("[EMAIL] 2fa-code → {Email}: {Code}", email, code);
        return Task.CompletedTask;
    }
}
