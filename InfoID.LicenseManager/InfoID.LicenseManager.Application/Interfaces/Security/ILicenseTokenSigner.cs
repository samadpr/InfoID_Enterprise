using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.Interfaces.Security;

/// <summary>
/// Signs online licenses (ECDSA P-256, token format <c>IIDL1.&lt;payload&gt;.&lt;signature&gt;</c>) with the
/// server-only private key. The key is never stored in the database, never logged and never leaves the server;
/// InfoID.Desktop holds only the public key, which can verify a signature but not create one.
/// </summary>
public interface ILicenseTokenSigner
{
    bool IsConfigured { get; }

    /// <exception cref="InvalidOperationException">No signing key is configured.</exception>
    string Sign(OnlineLicense license);
}
