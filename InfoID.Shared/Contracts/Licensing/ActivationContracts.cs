namespace InfoID.Shared.Contracts.Licensing;

/// <summary>
/// These DTOs are the ONLY thing InfoID.Desktop and the future
/// InfoID.AdminPortal / License Server actually share -- the shape of the
/// HTTPS messages between them (see the two-system architecture discussed
/// earlier: separate solutions, separate databases, linked only by this
/// contract). Neither side references the other's Domain entities.
/// Not wired to a real HTTP call yet -- InfoID.Licensing (added in a later
/// phase) will POST these to the License Server once it exists. Until then,
/// keep licensing stubbed/local as already planned.
/// </summary>
public class ActivationRequest
{
    public string ActivationCode { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
}

public class ActivationResponse
{
    public bool Success { get; set; }
    public string? SerialKey { get; set; }
    public string? LicenseTier { get; set; }
    public string? LicenseType { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? MaxCardsPerPeriod { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LicenseValidationRequest
{
    public string SerialKey { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public int CardsIssuedThisPeriod { get; set; }
}

public class LicenseValidationResponse
{
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty; // Active / Expired / GracePeriod / Deactivated
    public DateTime? NewExpiryDate { get; set; }
    public string? Message { get; set; }
}
