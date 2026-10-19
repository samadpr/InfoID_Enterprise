namespace InfoID.Shared.Contracts.Licensing;

/// <summary>
/// The HTTPS messages between InfoID.Desktop and InfoID.LicenseApi -- the ONLY thing the
/// two systems share besides InfoID.Licensing.Core. Neither side references the other's
/// database entities. Plain classes (no dependencies) so both projects can use them.
///
/// Licenses travel as signed tokens (InfoID.Licensing.Core.SignedLicense), so every
/// value that matters is inside the signature; these envelopes only carry it.
/// </summary>
public class OnlineActivationRequest
{
    public string ActivationCode { get; set; } = string.Empty;
    /// <summary>80-bit machine fingerprint, hex.</summary>
    public string MachineFingerprint { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
}

public class OnlineActivationResponse
{
    public bool Success { get; set; }
    /// <summary>Signed license token (IIDL1....) when Success.</summary>
    public string? License { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LicenseCheckInRequest
{
    public string ActivationCode { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public int CardsIssuedThisPeriod { get; set; }
    public string? AppVersion { get; set; }
}

public class LicenseCheckInResponse
{
    /// <summary>Active / Revoked / Deactivated / Unknown / Error.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Fresh signed license when Status is Active (may carry a renewed expiry).</summary>
    public string? License { get; set; }
    public string? Message { get; set; }
}

public class LicenseDeactivationRequest
{
    public string ActivationCode { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
}

public class LicenseDeactivationResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
