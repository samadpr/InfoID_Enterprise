using System.Net;
using System.Net.Mail;
using System.Text;
using InfoID.LicenseManager.Application.Interfaces.Notifications;
using Microsoft.Extensions.Configuration;

namespace InfoID.LicenseManager.Infrastructure.ExternalServices;

/// <summary>
/// Picks the sender from configuration. Settings come from the "EmailSettings" section of appsettings.json (or
/// user-secrets / environment variables such as EmailSettings__Password) and, taking priority, the INFOID_SMTP_*
/// environment variables the License API used. With no host configured every e-mail is written as a text file to
/// INFOID_MAIL_OUTBOX (default: a mail-outbox folder next to the app) so codes can be read while testing.
///
/// EmailSettings keys (alternative spellings in brackets are accepted): Host [SmtpServer, Server], Port,
/// Username [UserName, User], Password, From [FromEmail, SenderEmail, Email], FromName [SenderName, DisplayName],
/// EnableSsl [UseSsl, EnableSSL, UseStartTls]. Port 587 with STARTTLS is the default; implicit-TLS port 465 is not
/// supported by the .NET SMTP client.
/// </summary>
public static class EmailSenderFactory
{
    private const string Section = "EmailSettings";

    public static IEmailSender Create(IConfiguration configuration)
    {
        var settings = configuration.GetSection(Section);
        string? Pick(string? environmentName, params string[] keys)
        {
            if (environmentName is not null && !string.IsNullOrWhiteSpace(configuration[environmentName])) return configuration[environmentName];
            return keys.Select(k => settings[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        var host = Pick("INFOID_SMTP_HOST", "Host", "SmtpServer", "Server");
        if (string.IsNullOrWhiteSpace(host))
        {
            var folder = configuration["INFOID_MAIL_OUTBOX"];
            return new OutboxEmailSender(string.IsNullOrWhiteSpace(folder) ? Path.Combine(Directory.GetCurrentDirectory(), "mail-outbox") : folder);
        }

        var user = Pick("INFOID_SMTP_USER", "Username", "UserName", "User");
        var from = Pick("INFOID_SMTP_FROM", "From", "FromEmail", "SenderEmail", "Email") ?? user ?? "noreply@localhost";
        var password = Pick("INFOID_SMTP_PASSWORD", "Password");
        // Mailboxes such as Gmail sign in with the address itself: with a password but no user name, the sender address is the login.
        if (string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password)) user = from;
        var sslText = Pick("INFOID_SMTP_SSL", "EnableSsl", "UseSsl", "EnableSSL", "UseStartTls");
        return new SmtpEmailSender(
            host.Trim(),
            int.TryParse(Pick("INFOID_SMTP_PORT", "Port"), out var port) ? port : 587,
            user?.Trim(),
            password,
            from.Trim(),
            !string.Equals(sslText, "false", StringComparison.OrdinalIgnoreCase),
            Pick(null, "FromName", "SenderName", "DisplayName")?.Trim());
    }
}

/// <summary>Real delivery through an SMTP server (company mailbox or a transactional service). Port 587 + STARTTLS by default.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _password;
    private readonly string _from;
    private readonly string? _fromName;
    private readonly bool _ssl;

    public SmtpEmailSender(string host, int port, string? user, string? password, string from, bool ssl, string? fromName = null)
    {
        _host = host;
        _port = port;
        _user = user;
        _password = password;
        _from = from;
        _fromName = fromName;
        _ssl = ssl;
    }

    public bool IsRealDelivery => true;

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage(
            string.IsNullOrWhiteSpace(_fromName) ? new MailAddress(_from) : new MailAddress(_from, _fromName, Encoding.UTF8),
            new MailAddress(to))
        {
            Subject = subject,
            Body = textBody,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        using var client = new SmtpClient(_host, _port) { EnableSsl = _ssl, Timeout = 30_000 };
        if (!string.IsNullOrWhiteSpace(_user))
            client.Credentials = new NetworkCredential(_user, _password);
        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>Development: writes each e-mail to a text file instead of sending it.</summary>
public sealed class OutboxEmailSender : IEmailSender
{
    private readonly string _folder;

    public OutboxEmailSender(string folder) => _folder = folder;

    public bool IsRealDelivery => false;

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_folder);
        var safeTo = string.Concat(to.Select(c => char.IsLetterOrDigit(c) || c is '.' or '@' or '-' ? c : '_'));
        var file = Path.Combine(_folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{safeTo}.txt");
        await File.WriteAllTextAsync(file, $"To: {to}\nSubject: {subject}\nDate (UTC): {DateTime.UtcNow:u}\n\n{textBody}\n", cancellationToken);
    }
}
