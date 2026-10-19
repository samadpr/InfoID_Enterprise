namespace InfoID.Licensing.Core.Codecs;

/// <summary>
/// Crockford Base32: 5 bits per character using 0-9 and A-Z without I, L, O, U, so codes
/// are easy to read aloud and type. Decoding is forgiving the way humans need it:
/// case-insensitive, ignores dashes and spaces, and reads O as 0 and I/L as 1.
/// </summary>
public static class CrockfordBase32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    internal static string EncodeBits(bool[] bits)
    {
        var charCount = (bits.Length + 4) / 5;
        var chars = new char[charCount];
        for (var c = 0; c < charCount; c++)
        {
            var value = 0;
            for (var b = 0; b < 5; b++)
            {
                var index = c * 5 + b;
                value = (value << 1) | (index < bits.Length && bits[index] ? 1 : 0);
            }
            chars[c] = Alphabet[value];
        }
        return new string(chars);
    }

    /// <summary>Decodes exactly <paramref name="expectedChars"/> characters into
    /// expectedChars * 5 bits. Throws FormatException on wrong length or characters.</summary>
    internal static bool[] DecodeBits(string text, int expectedChars)
    {
        var normalized = Normalize(text);
        if (normalized.Length != expectedChars)
            throw new FormatException($"Expected {expectedChars} characters but found {normalized.Length}.");

        var bits = new bool[expectedChars * 5];
        for (var c = 0; c < normalized.Length; c++)
        {
            var value = Alphabet.IndexOf(normalized[c]);
            if (value < 0)
                throw new FormatException($"'{normalized[c]}' is not a valid character.");
            for (var b = 0; b < 5; b++)
                bits[c * 5 + b] = ((value >> (4 - b)) & 1) != 0;
        }
        return bits;
    }

    /// <summary>Upper-cases, strips separators and maps look-alike letters.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var raw in text)
        {
            if (raw is '-' or ' ' or '\t' or '\r' or '\n' or '_') continue;
            var ch = char.ToUpperInvariant(raw);
            builder.Append(ch switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => ch,
            });
        }
        return builder.ToString();
    }

    /// <summary>"ABCDEFGHIJ" -> "ABCDE-FGHIJ".</summary>
    public static string Group(string normalized, int groupSize)
    {
        var parts = new List<string>();
        for (var i = 0; i < normalized.Length; i += groupSize)
            parts.Add(normalized.Substring(i, Math.Min(groupSize, normalized.Length - i)));
        return string.Join('-', parts);
    }
}
