using System.Text;

namespace InfoID.CardEncoding.Nfc;

public enum NdefRecordKind { Uri, Text }

/// <summary>One NDEF record InfoID writes: a link (phones open it) or text.</summary>
public sealed record NdefRecord(NdefRecordKind Kind, string Value, string Language = "en")
{
    public static NdefRecord ForUri(string uri) => new(NdefRecordKind.Uri, uri);
    public static NdefRecord ForText(string text, string language = "en") => new(NdefRecordKind.Text, text, language);
}

/// <summary>NDEF (NFC Forum) encoding and decoding, plus the Type 2 tag TLV wrapping used
/// by NTAG / MIFARE Ultralight. Pure logic -- no hardware.</summary>
public static class NdefMessage
{
    // NFC Forum URI identifier codes (the common ones)
    private static readonly string[] UriPrefixes =
        { "", "http://www.", "https://www.", "http://", "https://", "tel:", "mailto:" };

    /// <summary>The records as one NDEF message.</summary>
    public static byte[] Encode(IReadOnlyList<NdefRecord> records)
    {
        if (records.Count == 0) throw new ArgumentException("Add at least one record.", nameof(records));

        var output = new List<byte>();
        for (var i = 0; i < records.Count; i++)
        {
            var (type, payload) = TypeAndPayload(records[i]);
            var isShort = payload.Length < 256;

            byte header = 0x01;                          // TNF 1 = NFC Forum well-known type
            if (i == 0) header |= 0x80;                  // MB: message begin
            if (i == records.Count - 1) header |= 0x40;  // ME: message end
            if (isShort) header |= 0x10;                 // SR: short record

            output.Add(header);
            output.Add((byte)type.Length);
            if (isShort)
            {
                output.Add((byte)payload.Length);
            }
            else
            {
                output.Add((byte)(payload.Length >> 24));
                output.Add((byte)(payload.Length >> 16));
                output.Add((byte)(payload.Length >> 8));
                output.Add((byte)payload.Length);
            }
            output.AddRange(type);
            output.AddRange(payload);
        }
        return output.ToArray();
    }

    /// <summary>The message as a Type 2 tag NDEF TLV plus terminator TLV.</summary>
    public static byte[] ToType2Tlv(byte[] message)
    {
        var tlv = new List<byte> { 0x03 };
        if (message.Length < 0xFF)
        {
            tlv.Add((byte)message.Length);
        }
        else
        {
            tlv.Add(0xFF);
            tlv.Add((byte)(message.Length >> 8));
            tlv.Add((byte)message.Length);
        }
        tlv.AddRange(message);
        tlv.Add(0xFE);
        return tlv.ToArray();
    }

    /// <summary>Bytes the records take on a Type 2 tag (TLVs, rounded up to whole 4-byte pages).
    /// Compare with the tag's user memory before writing.</summary>
    public static int SizeOnType2Tag(IReadOnlyList<NdefRecord> records) =>
        (ToType2Tlv(Encode(records)).Length + 3) / 4 * 4;

    /// <summary>Records found in Type 2 tag user memory (from page 4). Empty when none.</summary>
    public static IReadOnlyList<NdefRecord> DecodeFromType2Memory(byte[] memory)
    {
        var i = 0;
        while (i < memory.Length)
        {
            var tag = memory[i];
            if (tag == 0x00) { i++; continue; }  // NULL TLV
            if (tag == 0xFE) break;              // terminator
            if (i + 1 >= memory.Length) break;

            int length, headerLength;
            if (memory[i + 1] == 0xFF)
            {
                if (i + 3 >= memory.Length) break;
                length = (memory[i + 2] << 8) | memory[i + 3];
                headerLength = 4;
            }
            else
            {
                length = memory[i + 1];
                headerLength = 2;
            }

            var start = i + headerLength;
            if (start + length > memory.Length) break;
            if (tag == 0x03) return DecodeMessage(memory.AsSpan(start, length));
            i = start + length;                  // skip lock / memory control TLVs
        }
        return Array.Empty<NdefRecord>();
    }

    /// <summary>URI and Text records of an NDEF message; other record types are skipped.</summary>
    public static IReadOnlyList<NdefRecord> DecodeMessage(ReadOnlySpan<byte> message)
    {
        var records = new List<NdefRecord>();
        var i = 0;
        while (i < message.Length)
        {
            var header = message[i];
            var isShort = (header & 0x10) != 0;
            var hasId = (header & 0x08) != 0;
            var tnf = header & 0x07;
            var p = i + 1;

            if (p >= message.Length) break;
            int typeLength = message[p++];

            int payloadLength;
            if (isShort)
            {
                if (p >= message.Length) break;
                payloadLength = message[p++];
            }
            else
            {
                if (p + 4 > message.Length) break;
                payloadLength = (message[p] << 24) | (message[p + 1] << 16) | (message[p + 2] << 8) | message[p + 3];
                p += 4;
            }

            var idLength = 0;
            if (hasId)
            {
                if (p >= message.Length) break;
                idLength = message[p++];
            }
            if (payloadLength < 0 || p + typeLength + idLength + payloadLength > message.Length) break;

            var type = message.Slice(p, typeLength);
            p += typeLength + idLength;
            var payload = message.Slice(p, payloadLength);
            p += payloadLength;

            if (tnf == 0x01 && typeLength == 1 && payload.Length >= 1)
            {
                if (type[0] == (byte)'U')
                {
                    var prefix = payload[0] < UriPrefixes.Length ? UriPrefixes[payload[0]] : "";
                    records.Add(NdefRecord.ForUri(prefix + Encoding.UTF8.GetString(payload.Slice(1))));
                }
                else if (type[0] == (byte)'T')
                {
                    var languageLength = payload[0] & 0x3F;
                    var isUtf16 = (payload[0] & 0x80) != 0;
                    if (1 + languageLength <= payload.Length)
                    {
                        var language = Encoding.ASCII.GetString(payload.Slice(1, languageLength));
                        var textBytes = payload.Slice(1 + languageLength);
                        var text = isUtf16 ? Encoding.BigEndianUnicode.GetString(textBytes) : Encoding.UTF8.GetString(textBytes);
                        records.Add(NdefRecord.ForText(text, language));
                    }
                }
            }

            if ((header & 0x40) != 0) break;     // ME: last record
            i = p;
        }
        return records;
    }

    private static (byte[] Type, byte[] Payload) TypeAndPayload(NdefRecord record)
    {
        switch (record.Kind)
        {
            case NdefRecordKind.Uri:
                {
                    var uri = record.Value ?? string.Empty;
                    byte code = 0;
                    for (byte c = 1; c < UriPrefixes.Length; c++)
                        if (uri.StartsWith(UriPrefixes[c], StringComparison.OrdinalIgnoreCase) &&
                            UriPrefixes[c].Length > UriPrefixes[code].Length)
                            code = c;
                    var rest = Encoding.UTF8.GetBytes(uri[UriPrefixes[code].Length..]);
                    return (new[] { (byte)'U' }, new[] { code }.Concat(rest).ToArray());
                }

            case NdefRecordKind.Text:
                {
                    var language = Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(record.Language) ? "en" : record.Language);
                    if (language.Length > 0x3F) throw new ArgumentException("Language code is too long.");
                    var text = Encoding.UTF8.GetBytes(record.Value ?? string.Empty);
                    var status = (byte)language.Length;              // bit 7 = 0: UTF-8
                    return (new[] { (byte)'T' }, new[] { status }.Concat(language).Concat(text).ToArray());
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(record), record.Kind, "Unsupported record type.");
        }
    }
}
