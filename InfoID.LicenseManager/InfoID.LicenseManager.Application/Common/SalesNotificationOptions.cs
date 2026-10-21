namespace InfoID.LicenseManager.Application.Common;

/// <summary>Where staff are told about a new plan request (configuration INFOID_SALES_NOTIFY_EMAIL). Empty = nobody is e-mailed.</summary>
public sealed class SalesNotificationOptions
{
    public string? Email { get; set; }
}
