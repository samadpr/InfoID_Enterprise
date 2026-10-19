using InfoID.Licensing.Core;

namespace InfoID.Licensing.Server.Services;

/// <summary>
/// Provides the serial-key secrets. They are NEVER stored in the database: read from
/// environment variables INFOID_LICENSE_SECRET_V1 .. _V15 (base64, 32+ bytes), so a
/// leaked database backup can't be used to generate keys. The highest version present
/// signs new keys; all versions remain accepted for validation (rotation, FR-ADM-15).
/// </summary>
public sealed class LicenseSecretStore
{
    public const string VariablePrefix = "INFOID_LICENSE_SECRET_V";

    private readonly IReadOnlyList<LicenseSecret> _secrets;

    public LicenseSecretStore(IReadOnlyList<LicenseSecret> secrets) => _secrets = secrets;

    public static LicenseSecretStore FromEnvironment()
    {
        var secrets = new List<LicenseSecret>();
        for (byte version = 1; version <= 15; version++)
        {
            var value = Environment.GetEnvironmentVariable(VariablePrefix + version);
            if (!string.IsNullOrWhiteSpace(value))
                secrets.Add(LicenseSecret.FromBase64(version, value));
        }
        return new LicenseSecretStore(secrets);
    }

    public bool IsConfigured => _secrets.Count > 0;

    public IReadOnlyList<LicenseSecret> All => _secrets;

    public LicenseSecret Current => _secrets.Count > 0
        ? _secrets.MaxBy(s => s.Version)!
        : throw new InvalidOperationException(
            $"No license secret is configured. Set the environment variable {VariablePrefix}1 to a base64 value (see README).");
}
