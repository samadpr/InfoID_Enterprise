using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Interfaces.Security;
using Microsoft.Extensions.Configuration;

namespace InfoID.LicenseManager.Infrastructure.Cryptography;

/// <summary>
/// Signs online licenses with the server-only ECDSA private key, read from configuration key
/// INFOID_LICENSE_SIGNING_KEY (PEM text) or INFOID_LICENSE_SIGNING_KEY_FILE (path to a PEM file), the same names the
/// License API uses. The key is read once, kept in memory only, and never logged or put in an error message.
/// </summary>
public sealed class ConfigurationLicenseTokenSigner : ILicenseTokenSigner
{
    public const string PemKey = "INFOID_LICENSE_SIGNING_KEY";
    public const string FileKey = "INFOID_LICENSE_SIGNING_KEY_FILE";

    private readonly Lazy<string?> _privateKeyPem;

    public ConfigurationLicenseTokenSigner(IConfiguration configuration) =>
        _privateKeyPem = new Lazy<string?>(() => Load(configuration));

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_privateKeyPem.Value);

    public string Sign(OnlineLicense license) =>
        SignedLicense.Sign(license, _privateKeyPem.Value ?? throw new InvalidOperationException($"No signing key is configured. Set {FileKey}."));

    private static string? Load(IConfiguration configuration)
    {
        var pem = configuration[PemKey];
        if (!string.IsNullOrWhiteSpace(pem)) return pem;

        var file = configuration[FileKey];
        return !string.IsNullOrWhiteSpace(file) && File.Exists(file) ? File.ReadAllText(file) : null;
    }
}
