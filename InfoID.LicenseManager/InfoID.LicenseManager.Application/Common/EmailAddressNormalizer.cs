using System.Net.Mail;

namespace InfoID.LicenseManager.Application.Common;

public static class EmailAddressNormalizer
{
    /// <summary>Lower-case, trimmed address, or null when it is not a plausible e-mail address.</summary>
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim().ToLowerInvariant();
        if (trimmed.Length > 200) return null;
        try
        {
            return new MailAddress(trimmed).Address == trimmed && trimmed.Contains('.') ? trimmed : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
