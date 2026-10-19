using System.Security.Cryptography;
using InfoID.Licensing.Core.Codecs;

namespace InfoID.Licensing.Core;

/// <summary>
/// The code sold to the customer (FR-ADM-4): 16 characters, e.g. <c>7K3M-QX9D-2HFA-P8ZR</c>.
/// 64 random bits + a 16-bit checksum, so a typo is caught on the customer's screen
/// before anything is sent. It carries NO license terms -- tier, expiry and limits live
/// in the vendor database (LicenseCode row) and only reach the client inside the signed
/// serial key.
/// </summary>
public sealed class ActivationCode : IEquatable<ActivationCode>
{
    public const int CharLength = 16;
    private const int RandomBytes = 8;

    private readonly byte[] _bytes; // 8 random + 2 checksum

    private ActivationCode(byte[] bytes) => _bytes = bytes;

    /// <summary>Normalized form without dashes, e.g. 7K3MQX9D2HFAP8ZR. Store this.</summary>
    public string Value => CrockfordBase32.EncodeBits(ToBits());

    /// <summary>10 bytes (random + checksum) as embedded in soft codes and MAC input.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    public static ActivationCode Generate()
    {
        var random = RandomNumberGenerator.GetBytes(RandomBytes);
        return new ActivationCode(Combine(random, Checksum(random)));
    }

    public static bool TryParse(string? text, out ActivationCode? code)
    {
        code = null;
        try
        {
            var reader = new BitReader(CrockfordBase32.DecodeBits(text ?? string.Empty, CharLength));
            var bytes = reader.ReadBytes(RandomBytes + 2);
            var expected = Checksum(bytes.AsSpan(0, RandomBytes));
            if (bytes[RandomBytes] != expected[0] || bytes[RandomBytes + 1] != expected[1])
                return false;

            code = new ActivationCode(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static ActivationCode FromBytes(byte[] bytes)
    {
        if (bytes.Length != RandomBytes + 2) throw new FormatException("Invalid activation code bytes.");
        var expected = Checksum(bytes.AsSpan(0, RandomBytes));
        if (bytes[RandomBytes] != expected[0] || bytes[RandomBytes + 1] != expected[1])
            throw new FormatException("The activation code inside this request is damaged.");
        return new ActivationCode(bytes);
    }

    private bool[] ToBits()
    {
        var writer = new BitWriter();
        writer.WriteBytes(_bytes);
        return writer.ToBits();
    }

    private static byte[] Checksum(ReadOnlySpan<byte> random)
    {
        Span<byte> input = stackalloc byte[RandomBytes + 9];
        "InfoIDAC-"u8.CopyTo(input);
        random.CopyTo(input[9..]);
        return SHA256.HashData(input)[..2];
    }

    private static byte[] Combine(byte[] random, byte[] checksum) => [.. random, .. checksum];

    /// <summary>Display form: XXXX-XXXX-XXXX-XXXX.</summary>
    public override string ToString() => CrockfordBase32.Group(Value, 4);

    public bool Equals(ActivationCode? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    public override bool Equals(object? obj) => Equals(obj as ActivationCode);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}
