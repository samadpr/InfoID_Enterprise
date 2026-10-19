using System.Net;
using System.Net.Mail;
using System.Text;

namespace InfoID.Licensing.Server.Services;

/// <summary>Sends customer/staff emails (verification codes, request updates).</summary>
public interface IEmailSender
{
    /// <summary>True when emails really leave the server (SMTP), false for the local
    /// test outbox.</summary>
    bool IsRealDelivery { get; }

    Task SendAsync(string to, string subject, string textBody, CancellationToken ct = default);
}

/// <summary>
/// Picks the sender from environment variables:
///   INFOID_SMTP_HOST (+ INFOID_SMTP_PORT, INFOID_SMTP_USER, INFOID_SMTP_PASSWORD,
///   INFOID_SMTP_FROM, INFOID_SMTP_SSL=true/false)  -> real SMTP delivery.
///   Otherwise -> OutboxEmailSender: each email is written as a .txt file to
///   INFOID_MAIL_OUTBOX (default: mail-outbox in the working folder -- the
///   InfoID.LicenseApi project folder when started from Visual Studio) so codes can be read while testing.
/// </summary>
public static class EmailSenderFactory
{
    public static IEmailSender FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("INFOID_SMTP_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            var folder = Environment.GetEnvironmentVariable("INFOID_MAIL_OUTBOX");
            return new OutboxEmailSender(string.IsNullOrWhiteSpace(folder) ? Path.Combine(Directory.GetCurrentDirectory(), "mail-outbox") : folder);
        }

        return new SmtpEmailSender(
            host,
            int.TryParse(Environment.GetEnvironmentVariable("INFOID_SMTP_PORT"), out var port) ? port : 587,
            Environment.GetEnvironmentVariable("INFOID_SMTP_USER"),
            Environment.GetEnvironmentVariable("INFOID_SMTP_PASSWORD"),
            Environment.GetEnvironmentVariable("INFOID_SMTP_FROM") ?? Environment.GetEnvironmentVariable("INFOID_SMTP_USER") ?? "noreply@localhost",
            !string.Equals(Environment.GetEnvironmentVariable("INFOID_SMTP_SSL"), "false", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Real delivery through an SMTP server (company mailbox or a transactional
/// service such as Brevo/SendGrid/SES). Port 587 + STARTTLS by default.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _password;
    private readonly string _from;
    private readonly bool _ssl;

    public SmtpEmailSender(string host, int port, string? user, string? password, string from, bool ssl)
    {
        _host = host;
        _port = port;
        _user = user;
        _password = password;
        _from = from;
        _ssl = ssl;
    }

    public bool IsRealDelivery => true;

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken ct = default)
    {
        using var message = new MailMessage(_from, to, subject, textBody) { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        using var client = new SmtpClient(_host, _port) { EnableSsl = _ssl };
        if (!string.IsNullOrWhiteSpace(_user))
            client.Credentials = new NetworkCredential(_user, _password);
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>Development: writes each email to a text file instead of sending it.</summary>
public sealed class OutboxEmailSender : IEmailSender
{
    private readonly string _folder;

    public OutboxEmailSender(string folder) => _folder = folder;

    public bool IsRealDelivery => false;

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_folder);
        var safeTo = string.Concat(to.Select(c => char.IsLetterOrDigit(c) || c is '.' or '@' or '-' ? c : '_'));
        var file = Path.Combine(_folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{safeTo}.txt");
        await File.WriteAllTextAsync(file, $"To: {to}\nSubject: {subject}\nDate (UTC): {DateTime.UtcNow:u}\n\n{textBody}\n", ct);
    }
}
