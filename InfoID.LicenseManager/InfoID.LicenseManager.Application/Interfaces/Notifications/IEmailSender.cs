namespace InfoID.LicenseManager.Application.Interfaces.Notifications;

/// <summary>Sends plain-text e-mail to customers (verification codes, request decisions, activation codes).</summary>
public interface IEmailSender
{
    /// <summary>True when mail really leaves the server (SMTP); false for the development outbox folder.</summary>
    bool IsRealDelivery { get; }

    Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default);
}
