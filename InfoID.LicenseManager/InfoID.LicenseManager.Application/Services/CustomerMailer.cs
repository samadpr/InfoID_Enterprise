using InfoID.LicenseManager.Application.Interfaces.Notifications;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.Shared.Logging;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

/// <summary>Sends customer e-mail without ever failing the workflow that triggered it: a decision that was
/// already committed must not turn into an error because the mail server was down. A failure is logged (with
/// the address masked) and audited, and the caller is told so it can tell the user.</summary>
public sealed class CustomerMailer
{
    private readonly IEmailSender _email;
    private readonly IAuditService _audit;
    private readonly ILogger<CustomerMailer> _logger;

    public CustomerMailer(IEmailSender email, IAuditService audit, ILogger<CustomerMailer> logger)
    {
        _email = email;
        _audit = audit;
        _logger = logger;
    }

    /// <param name="failureAuditAction">Audit action written when sending fails.</param>
    /// <param name="failureDetails">Builds the audit details from the error message.</param>
    public async Task<bool> TrySendAsync(string to, string subject, string body, string failureAuditAction,
        Func<string, object> failureDetails, CancellationToken cancellationToken)
    {
        try
        {
            await _email.SendAsync(to, subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "E-mail \"{Subject}\" to {Recipient} could not be sent", subject, LogText.MaskEmail(to));
            await _audit.LogAsync(failureAuditAction, failureDetails(ex.GetBaseException().Message), cancellationToken: cancellationToken);
            return false;
        }
    }
}
