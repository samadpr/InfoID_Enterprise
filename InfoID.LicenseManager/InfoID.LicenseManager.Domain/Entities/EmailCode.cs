namespace InfoID.LicenseManager.Domain.Entities;

public static class EmailCodePurpose
{
    public const string VerifyEmail = "VerifyEmail";
    public const string ResetPassword = "ResetPassword";
}

/// <summary>A one-time 6-digit code sent by email (verification / password reset).
/// Only a hash is stored; expires and allows limited attempts.</summary>
public class EmailCode : EntityBase
{
    public long CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public string Purpose { get; set; } = EmailCodePurpose.VerifyEmail;
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? UsedUtc { get; set; }
}
