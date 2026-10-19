using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfoID.Licensing.Core;

/// <summary>
/// The license contents for ONLINE activation. Unlike the 20-character serial key, this
/// never has to be typed, so it can carry a real public-key signature.
/// </summary>
public sealed record OnlineLicense
{
    /// <summary>Vendor ActivationRecord.Id -- identifies this machine's activation.</summary>
    public long LicenseId { get; init; }
    /// <summary>Normalized 16-character activation code (no dashes).</summary>
    public string ActivationCode { get; init; } = "";
    /// <summary>80-bit machine fingerprint, hex.</summary>
    public string Fingerprint { get; init; } = "";
    public LicenseTier Tier { get; init; }
    public LicenseType Type { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public int? MaxCardsPerPeriod { get; init; }
    public CardLimitPeriod LimitPeriod { get; init; }
    public string? OrganizationName { get; init; }
    public DateTime IssuedUtc { get; init; }
    /// <summary>InfoID must check in successfully before this moment, or printing is
    /// paused until it can (FR-LIC-13). Renewed at every check-in.</summary>
    public DateTime CheckInDeadlineUtc { get; init; }

    public SerialKeyTerms ToTerms() => new(Tier, Type, ExpiryDate, MaxCardsPerPeriod, LimitPeriod);
}

/// <summary>
/// Signs and verifies <see cref="OnlineLicense"/> tokens with ECDSA P-256.
///
/// Token format: <c>IIDL1.&lt;base64url(json)&gt;.&lt;base64url(signature)&gt;</c>
///
/// The PRIVATE key exists only on the License API server. InfoID contains only the
/// PUBLIC key, which can check a signature but cannot create one -- so, unlike the
/// offline 20-character key, nothing inside InfoID lets anyone forge a license.
/// </summary>
public static class SignedLicense
{
    public const string Prefix = "IIDL1.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool IsSignedLicense(string? text) => text?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>Server side: sign with the private key (PEM, "BEGIN PRIVATE KEY" or
    /// "BEGIN EC PRIVATE KEY").</summary>
    public static string Sign(OnlineLicense license, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var payload = JsonSerializer.SerializeToUtf8Bytes(license, Json);
        var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        return Prefix + Base64Url(payload) + "." + Base64Url(signature);
    }

    /// <summary>Client side: returns the license if the signature is valid for the
    /// public key (PEM, "BEGIN PUBLIC KEY"), otherwise null with a reason.</summary>
    public static OnlineLicense? Verify(string? token, string publicKeyPem, out string? error)
    {
        error = null;
        if (!IsSignedLicense(token))
        {
            error = "Not an InfoID online license.";
            return null;
        }

        var parts = token![Prefix.Length..].Split('.');
        if (parts.Length != 2)
        {
            error = "The license is damaged.";
            return null;
        }

        try
        {
            var payload = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                error = "The license signature is not valid.";
                return null;
            }

            return JsonSerializer.Deserialize<OnlineLicense>(payload, Json)
                   ?? throw new FormatException("Empty license.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            error = "The license is damaged: " + ex.Message;
            return null;
        }
    }

    /// <summary>Creates a new signing key pair (setup, once). Keep the private key on
    /// the server only; embed the public key in InfoID.</summary>
    public static (string PrivateKeyPem, string PublicKeyPem) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
