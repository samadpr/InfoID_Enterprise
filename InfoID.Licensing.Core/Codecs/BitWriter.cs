namespace InfoID.Licensing.Core.Codecs;

/// <summary>Writes values most-significant-bit first into a growing bit sequence.
/// Readability over speed: codes are tens of bits long, so a bool list is fine.</summary>
internal sealed class BitWriter
{
    private readonly List<bool> _bits = new();

    public int Length => _bits.Count;

    public void Write(ulong value, int bitCount)
    {
        if (bitCount is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        if (bitCount < 64 && value >> bitCount != 0)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value {value} does not fit in {bitCount} bits.");

        for (var i = bitCount - 1; i >= 0; i--)
            _bits.Add(((value >> i) & 1UL) != 0);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            Write(b, 8);
    }

    public bool[] ToBits() => _bits.ToArray();

    /// <summary>Packs the bits into bytes, zero-padding the last byte.</summary>
    public byte[] ToBytes() => BitPacking.ToBytes(_bits);
}

/// <summary>Reads values most-significant-bit first from a bit sequence.</summary>
internal sealed class BitReader
{
    private readonly bool[] _bits;
    private int _position;

    public BitReader(bool[] bits) => _bits = bits;

    public int Remaining => _bits.Length - _position;

    public ulong Read(int bitCount)
    {
        if (bitCount is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        if (bitCount > Remaining)
            throw new FormatException("Code is too short.");

        ulong value = 0;
        for (var i = 0; i < bitCount; i++)
            value = (value << 1) | (_bits[_position++] ? 1UL : 0UL);
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        var result = new byte[count];
        for (var i = 0; i < count; i++)
            result[i] = (byte)Read(8);
        return result;
    }
}

internal static class BitPacking
{
    public static byte[] ToBytes(IReadOnlyList<bool> bits)
    {
        var bytes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        }
        return bytes;
    }
}
