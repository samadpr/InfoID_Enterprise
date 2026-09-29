using System.Security.Cryptography;
using InfoID.Licensing.Core.Codecs;

namespace InfoID.Licensing.Core;

/// <summary>The license terms carried inside a serial key.</summary>
/// <param name="MaxCardsPerPeriod">null = unlimited. Otherwise a multiple of 100, at most
/// <see cref="SerialKey.MaxCardLimit"/> (the key stores it in hundreds, 10 bits).</param>
public sealed record SerialKeyTerms(
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod)
{
    /// <summary>Null when valid, otherwise a user-facing reason.</summary>
    public string? Validate()
    {
        if (Type == LicenseType.Perpetual && ExpiryDate is not null)
            return "A perpetual license must not have an expiry date.";
        if (Type != LicenseType.Perpetual && ExpiryDate is null)
            return "Subscription and trial licenses need an expiry date.";
        if (ExpiryDate is { } expiry && expiry <= LicenseDates.Epoch)
            return "The expiry date is too early.";
        if (MaxCardsPerPeriod is { } max && (max <= 0 || max % 100 != 0 || max > SerialKey.MaxCardLimit))
            return $"The card limit must be a multiple of 100 between 100 and {SerialKey.MaxCardLimit:N0}, or unlimited.";
        return null;
    }
}

/// <summary>Result of checking a serial key on the customer's machine.</summary>
public sealed record SerialKeyValidation(bool IsValid, SerialKeyTerms? Terms, string? Error)
{
    public static SerialKeyValidation Fail(string error) => new(false, null, error);
}

/// <summary>
/// The 20-character serial key (<c>XXXXX-XXXXX-XXXXX-XXXXX</c>) that unlocks InfoID on
/// ONE machine (FR-LIC-5 to FR-LIC-8).
///
/// Layout (100 bits = 20 characters):
///   secret version 4 | tier 2 | type 2 | expiry day 16 | card limit (x100) 10 |
///   limit period 1 | reserved 1 | MAC 64
///
/// The MAC is HMAC-SHA256 over the terms + activation code + machine fingerprint,
/// truncated to 64 bits. So a key only validates for the exact activation code and
/// machine it was issued for (FR-LIC-7), and changing any term (tier, expiry, limit)
/// breaks it. Forging a key by guessing is a 1-in-2^64 chance per attempt.
///
/// Honest limit: 20 characters is too short for a public-key signature (RSA-2048 needs
/// ~410 characters, Ed25519 ~103), so this is a shared-secret MAC and the desktop app
/// has to contain the secret to verify keys offline. Someone who reverse-engineers the
/// app could extract it and write a key generator. Mitigations: obfuscate the secret
/// in the client, rotate it via the version field, and have online re-validation
/// (FR-LIC-13) check every activation against the vendor database, where revoked or
/// unknown activations are caught.
/// </summary>
public static class SerialKey
{
    public const int CharLength = 20;
    public const int MaxCardLimit = 1023 * 100;

    public static string Generate(SerialKeyTerms terms, ActivationCode activationCode, MachineFingerprint fingerprint, LicenseSecret secret)
    {
        var problem = terms.Validate();
        if (problem is not null) throw new ArgumentException(problem, nameof(terms));

        var payload = WritePayload(secret.Version, terms);
        var mac = ComputeMac(payload, activationCode, fingerprint, secret);

        var writer = new BitWriter();
        foreach (var bit in payload) writer.Write(bit ? 1UL : 0UL, 1);
        writer.Write(mac, 64);
        return CrockfordBase32.Group(CrockfordBase32.EncodeBits(writer.ToBits()), 5);
    }

    /// <summary>Verifies a typed/pasted key for THIS machine and activation code.
    /// <paramref name="secrets"/> holds every secret version the client accepts.</summary>
    public static SerialKeyValidation Validate(
        string? serialKey, ActivationCode activationCode, MachineFingerprint fingerprint, IEnumerable<LicenseSecret> secrets)
    {
        bool[] bits;
        try
        {
            bits = CrockfordBase32.DecodeBits(serialKey ?? string.Empty, CharLength);
        }
        catch (FormatException)
        {
            return SerialKeyValidation.Fail("The serial key must be 20 characters (letters and numbers). Check it was typed exactly.");
        }

        var reader = new BitReader(bits);
        var version = (byte)reader.Read(4);
        var tier = (LicenseTier)reader.Read(2);
        var type = (LicenseType)reader.Read(2);
        var expiryDay = (ushort)reader.Read(16);
        var limitHundreds = (int)reader.Read(10);
        var limitPeriod = (CardLimitPeriod)reader.Read(1);
        var reserved = reader.Read(1);
        var mac = reader.Read(64);

        var secret = secrets.FirstOrDefault(s => s.Version == version);
        if (secret is null)
            return SerialKeyValidation.Fail("This serial key was issued for a different version of InfoID. Please contact support.");

        var payload = bits[..36];
        var expected = ComputeMac(payload, activationCode, fingerprint, secret);

        Span<byte> actualBytes = stackalloc byte[8];
        Span<byte> expectedBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(actualBytes, mac);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(expectedBytes, expected);

        if (!CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
            return SerialKeyValidation.Fail("This serial key is not valid for this activation code on this computer.");

        if (reserved != 0 || !Enum.IsDefined(tier) || !Enum.IsDefined(type))
            return SerialKeyValidation.Fail("This serial key uses features this version of InfoID doesn't support. Please update InfoID.");

        var terms = new SerialKeyTerms(
            tier,
            type,
            LicenseDates.FromDay(expiryDay),
            limitHundreds == 0 ? null : limitHundreds * 100,
            limitPeriod);

        return new SerialKeyValidation(true, terms, null);
    }

    private static bool[] WritePayload(byte version, SerialKeyTerms terms)
    {
        var writer = new BitWriter();
        writer.Write(version, 4);
        writer.Write((ulong)terms.Tier, 2);
        writer.Write((ulong)terms.Type, 2);
        writer.Write(terms.ExpiryDate is { } expiry ? LicenseDates.ToDay(expiry) : 0UL, 16);
        writer.Write(terms.MaxCardsPerPeriod is { } max ? (ulong)(max / 100) : 0UL, 10);
        writer.Write((ulong)terms.LimitPeriod, 1);
        writer.Write(0, 1); // reserved
        return writer.ToBits();
    }

    private static ulong ComputeMac(bool[] payloadBits, ActivationCode activationCode, MachineFingerprint fingerprint, LicenseSecret secret)
    {
        byte[] input =
        [
            .. "InfoIDSK1|"u8.ToArray(),
            .. BitPacking.ToBytes(payloadBits),
            .. activationCode.Bytes.ToArray(),
            .. fingerprint.Hash.ToArray(),
        ];
        var hash = HMACSHA256.HashData(secret.Key, input);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash);
    }
}
