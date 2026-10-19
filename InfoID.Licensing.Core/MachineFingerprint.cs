using System.Security.Cryptography;
using System.Text;

namespace InfoID.Licensing.Core;

/// <summary>
/// The machine identity a license is bound to (FR-LIC-3, FR-LIC-7), reduced to an
/// 80-bit hash so it fits in the soft code. The desktop app collects the hardware
/// components (see its fingerprint collector); this type only normalizes and hashes
/// them, so the client and server always agree on the result.
/// </summary>
public sealed class MachineFingerprint
{
    public const int HashLength = 10; // 80 bits

    private readonly byte[] _hash;

    private MachineFingerprint(byte[] hash) => _hash = hash;

    public ReadOnlySpan<byte> Hash => _hash;

    /// <summary>Uppercase hex, e.g. for the vendor database and support screens.</summary>
    public string Hex => Convert.ToHexString(_hash);

    /// <summary>Masked form for the License Status page (FR-LIC-9).</summary>
    public string Masked => Hex[..4] + new string('*', Hex.Length - 8) + Hex[^4..];

    /// <summary>Builds the fingerprint from named hardware components, e.g.
    /// ("MachineGuid", "..."), ("BoardSerial", "..."). Order-independent, trims and
    /// upper-cases values, ignores empty ones, and needs at least one real value.</summary>
    public static MachineFingerprint FromComponents(IEnumerable<KeyValuePair<string, string?>> components)
    {
        var parts = components
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => $"{c.Key.Trim().ToUpperInvariant()}={c.Value!.Trim().ToUpperInvariant()}")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (parts.Count == 0)
            throw new InvalidOperationException("No hardware identifiers could be read on this machine.");

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("InfoIDFP1|" + string.Join('|', parts)));
        return new MachineFingerprint(digest[..HashLength]);
    }

    public static MachineFingerprint FromHash(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != HashLength) throw new ArgumentException($"Fingerprint hash must be {HashLength} bytes.", nameof(hash));
        return new MachineFingerprint(hash.ToArray());
    }

    public static MachineFingerprint FromHex(string hex) => FromHash(Convert.FromHexString(hex));

    public override string ToString() => Hex;
}
