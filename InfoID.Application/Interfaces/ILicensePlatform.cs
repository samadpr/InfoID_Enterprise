using InfoID.Licensing.Core;

namespace InfoID.Application.Interfaces;

/// <summary>Reads this computer's hardware identity (FR-LIC-3). Implemented by the
/// desktop app per operating system; the license service only needs the result.</summary>
public interface IMachineFingerprintProvider
{
    MachineFingerprint GetFingerprint();
}

/// <summary>The serial-key secrets this build of InfoID accepts (same values as the
/// vendor's INFOID_LICENSE_SECRET_Vn). Implemented by the desktop app, which embeds
/// them obfuscated.</summary>
public interface ILicenseSecretProvider
{
    IReadOnlyList<LicenseSecret> GetSecrets();

    /// <summary>Public key (PEM) that verifies ONLINE licenses signed by the License
    /// API. Public keys can't sign, so embedding it is safe. Null if not configured.</summary>
    string? GetSigningPublicKeyPem();
}
