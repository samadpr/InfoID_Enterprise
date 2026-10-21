using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.Interfaces.Security;

/// <summary>
/// The versioned HMAC secrets behind 20-character serial keys. They are never stored in the database:
/// a leaked backup must not be enough to forge keys. The highest version signs new keys; every
/// version stays accepted for validation (rotation, FR-ADM-15).
/// </summary>
public interface ILicenseSecretProvider
{
    bool IsConfigured { get; }

    IReadOnlyList<LicenseSecret> All { get; }

    /// <summary>The secret new keys are made with.</summary>
    /// <exception cref="InvalidOperationException">No secret is configured.</exception>
    LicenseSecret Current { get; }
}
