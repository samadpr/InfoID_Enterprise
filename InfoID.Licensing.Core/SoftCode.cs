using System.Security.Cryptography;
using InfoID.Licensing.Core.Codecs;

namespace InfoID.Licensing.Core;

/// <summary>
/// The request (soft) code the customer sends to the vendor for OFFLINE activation
/// (FR-LIC-4, FR-LIC-6): 40 characters in 8 groups of 5, e.g.
/// <c>1ZK8M-...</c>. It carries the activation code, the 80-bit machine fingerprint and
/// the request date, protected by a checksum so typing mistakes are detected.
///
/// Layout (196 bits, 4 zero padding bits to fill 40 characters):
///   version 4 | activation code 80 | fingerprint 80 | request day 16 | checksum 16
///
/// Design note vs SRS FR-LIC-4 (RSA-encrypted soft code): an RSA-2048 ciphertext is ~350
/// characters, which can't be read over the phone. The contents are not secret (a hash
/// and the customer's own code), and what makes the scheme secure is the serial key that
/// comes back, not the request -- so the request is checksummed, not encrypted.
/// </summary>
public sealed record SoftCode(ActivationCode ActivationCode, MachineFingerprint Fingerprint, DateOnly RequestDate)
{
    public const int CharLength = 40;
    private const int FormatVersion = 1;

    public static SoftCode Create(ActivationCode activationCode, MachineFingerprint fingerprint) =>
        new(activationCode, fingerprint, DateOnly.FromDateTime(DateTime.UtcNow));

    /// <summary>Display form: 8 groups of 5 characters.</summary>
    public string Encode()
    {
        var writer = new BitWriter();
        WriteBody(writer);
        writer.Write(Checksum(writer.ToBytes()), 16);
        writer.Write(0, 4); // padding to 200 bits = 40 characters
        return CrockfordBase32.Group(CrockfordBase32.EncodeBits(writer.ToBits()), 5);
    }

    public static bool TryParse(string? text, out SoftCode? softCode, out string? error)
    {
        softCode = null;
        error = null;
        try
        {
            var bits = CrockfordBase32.DecodeBits(text ?? string.Empty, CharLength);
            var reader = new BitReader(bits);

            var version = (int)reader.Read(4);
            if (version != FormatVersion)
            {
                error = $"Unsupported request code version {version}. The customer may need a newer InfoID version.";
                return false;
            }

            var codeBytes = reader.ReadBytes(10);
            var fingerprintBytes = reader.ReadBytes(MachineFingerprint.HashLength);
            var day = (ushort)reader.Read(16);
            var checksum = (ushort)reader.Read(16);
            var padding = reader.Read(4);

            var body = new BitWriter();
            body.Write((ulong)version, 4);
            body.WriteBytes(codeBytes);
            body.WriteBytes(fingerprintBytes);
            body.Write(day, 16);

            if (padding != 0 || checksum != Checksum(body.ToBytes()))
            {
                error = "The request code is not valid. Check it was copied or typed exactly.";
                return false;
            }

            var date = LicenseDates.FromDay(day);
            if (date is null)
            {
                error = "The request code has no request date.";
                return false;
            }

            softCode = new SoftCode(ActivationCode.FromBytes(codeBytes), MachineFingerprint.FromHash(fingerprintBytes), date.Value);
            return true;
        }
        catch (FormatException ex)
        {
            error = $"The request code is not valid: {ex.Message}";
            return false;
        }
    }

    private void WriteBody(BitWriter writer)
    {
        writer.Write(FormatVersion, 4);
        writer.WriteBytes(ActivationCode.Bytes);
        writer.WriteBytes(Fingerprint.Hash);
        writer.Write(LicenseDates.ToDay(RequestDate), 16);
    }

    private static ushort Checksum(byte[] body)
    {
        byte[] input = [.. "InfoIDSC|"u8.ToArray(), .. body];
        var hash = SHA256.HashData(input);
        return (ushort)((hash[0] << 8) | hash[1]);
    }
}
