using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Interfaces.Security;
using Microsoft.Extensions.Configuration;

namespace InfoID.LicenseManager.Infrastructure.Cryptography;

/// <summary>
/// Reads the serial-key secrets from configuration keys INFOID_LICENSE_SECRET_V1 ... _V15 (base64,
/// 32+ bytes). Environment variables use exactly those names, as in InfoID.Licensing.Server, so an
/// existing deployment's secrets carry over; in development the same keys work in user-secrets.
/// A secret is never written to the database, a log, or an error message.
/// </summary>
public sealed class ConfigurationLicenseSecretProvider : ILicenseSecretProvider
{
    public const string KeyPrefix = "INFOID_LICENSE_SECRET_V";

    private readonly IReadOnlyList<LicenseSecret> _secrets;

    public ConfigurationLicenseSecretProvider(IConfiguration configuration)
    {
        var secrets = new List<LicenseSecret>();
        for (byte version = 1; version <= 15; version++)
        {
            var name = KeyPrefix + version;
            var value = configuration[name];
            if (string.IsNullOrWhiteSpace(value)) continue;

            try
            {
                secrets.Add(LicenseSecret.FromBase64(version, value));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Name the setting, never its value.
                throw new InvalidOperationException($"{name} is not a valid license secret (base64, at least {LicenseSecret.MinKeyLength} bytes).");
            }
        }

        _secrets = secrets;
    }

    public bool IsConfigured => _secrets.Count > 0;

    public IReadOnlyList<LicenseSecret> All => _secrets;

    public LicenseSecret Current => _secrets.Count > 0
        ? _secrets.MaxBy(s => s.Version)!
        : throw new InvalidOperationException($"No license secret is configured. Set {KeyPrefix}1 to a base64 value.");
}
