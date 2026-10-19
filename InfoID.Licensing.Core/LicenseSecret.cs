namespace InfoID.Licensing.Core;

/// <summary>
/// A versioned HMAC key used to create and verify 20-character serial keys. The version
/// (1-15) is written into every key, so a new secret can be rolled out later while keys
/// issued with older secrets keep validating (FR-ADM-15 "rotation support").
///
/// The vendor side reads it from a protected location (environment variable or key
/// file); the desktop app must embed it. That embedding is the known limit of any
/// short, offline-typable key -- see SerialKey's remarks.
/// </summary>
public sealed class LicenseSecret
{
    public const int MinKeyLength = 32;

    public LicenseSecret(byte version, byte[] key)
    {
        if (version is < 1 or > 15) throw new ArgumentOutOfRangeException(nameof(version), "Secret version must be 1-15.");
        if (key is null || key.Length < MinKeyLength) throw new ArgumentException($"Secret must be at least {MinKeyLength} bytes.", nameof(key));
        Version = version;
        Key = key;
    }

    public byte Version { get; }

    public byte[] Key { get; }

    public static LicenseSecret FromBase64(byte version, string base64) => new(version, Convert.FromBase64String(base64.Trim()));

    /// <summary>A new random 256-bit secret, base64-encoded, for initial setup.</summary>
    public static string GenerateBase64() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
