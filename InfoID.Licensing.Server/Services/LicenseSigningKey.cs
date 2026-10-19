namespace InfoID.Licensing.Server.Services;

/// <summary>
/// The ECDSA private key the License API signs online licenses with
/// (InfoID.Licensing.Core.SignedLicense). Server only -- never on staff PCs, never in
/// InfoID, never in the database. Read from INFOID_LICENSE_SIGNING_KEY (PEM text) or
/// INFOID_LICENSE_SIGNING_KEY_FILE (path to a PEM file).
/// </summary>
public sealed class LicenseSigningKey
{
    public const string PemVariable = "INFOID_LICENSE_SIGNING_KEY";
    public const string FileVariable = "INFOID_LICENSE_SIGNING_KEY_FILE";

    public LicenseSigningKey(string privateKeyPem) => PrivateKeyPem = privateKeyPem;

    public string PrivateKeyPem { get; }

    public static LicenseSigningKey? FromEnvironment()
    {
        var pem = Environment.GetEnvironmentVariable(PemVariable);
        if (string.IsNullOrWhiteSpace(pem))
        {
            var file = Environment.GetEnvironmentVariable(FileVariable);
            if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                pem = File.ReadAllText(file);
        }
        return string.IsNullOrWhiteSpace(pem) ? null : new LicenseSigningKey(pem);
    }
}
