namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>A signed-in app session. The app holds a random token; only its SHA-256
/// hash is stored, so a database leak doesn't expose usable tokens.</summary>
public class CustomerSession : EntityBase
{
    public long CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? CreatedFromIp { get; set; }
    public string? AppVersion { get; set; }
}
