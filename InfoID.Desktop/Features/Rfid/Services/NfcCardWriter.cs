using System;
using System.Collections.Generic;
using System.Linq;
using InfoID.Application.Services;
using InfoID.CardEncoding.Contactless;
using InfoID.CardEncoding.Nfc;

namespace InfoID.Desktop.Features.Rfid.Services;

/// <summary>What is on the tag right now.</summary>
public sealed record NfcTagInfo(
    string Uid, string ModelName, int CapacityBytes, bool IsFormatted, bool IsReadOnly,
    IReadOnlyList<NdefRecord> Records, bool HasUnreadableData)
{
    public bool HasData => Records.Count > 0 || HasUnreadableData;

    /// <summary>The tag already holds exactly this content (nothing would change).</summary>
    public bool Holds(IReadOnlyList<NdefRecord> records) => !HasUnreadableData && Records.SequenceEqual(records);

    public IEnumerable<string> DescribeContent()
    {
        foreach (var r in Records)
            yield return r.Kind == NdefRecordKind.Uri ? "Link: " + r.Value : "Text: " + r.Value;
        if (HasUnreadableData)
            yield return "Data that isn't a standard NFC (NDEF) message";
    }
}

/// <summary>
/// Reads and writes NFC Forum Type 2 tags on the selected reader. Independent from printing,
/// so the Print dialog can reuse it later (Step 5). Never touches the UID, lock bits or
/// configuration pages; every write is read back and compared (Type2Tag.WriteNdef).
/// All methods block, so call them from Task.Run. They throw ContactlessException.
/// </summary>
public sealed class NfcCardWriter
{
    private readonly IContactlessReaderService _readers;

    public NfcCardWriter(IContactlessReaderService readers) => _readers = readers;

    public NfcTagInfo Inspect()
    {
        using var connection = _readers.Connect(exclusive: false);
        return Describe(connection, out _);
    }

    /// <summary>Writes only if the tag on the reader is still <paramref name="expectedUid"/>.</summary>
    public NfcTagInfo Write(string expectedUid, IReadOnlyList<NdefRecord> records)
    {
        using var connection = _readers.Connect(exclusive: true);
        var info = Describe(connection, out var tag);
        if (!string.Equals(info.Uid, expectedUid, StringComparison.OrdinalIgnoreCase))
            throw new ContactlessException(ContactlessError.CardRemoved,
                "A different tag is on the reader now. Place the tag again to check it first.", null);

        tag.WriteNdef(records); // checks format, lock and space; writes; reads back and compares
        return info;
    }

    private static NfcTagInfo Describe(ICardConnection connection, out Type2Tag tag)
    {
        if (!Type2Tag.TryOpen(connection, out var opened) || opened is null)
            throw new ContactlessException(ContactlessError.NotSupported,
                "This card isn't an NFC tag InfoID can write. Use NTAG213, NTAG215, NTAG216 or MIFARE Ultralight.", null);
        tag = opened;

        var records = new List<NdefRecord>();
        var unreadable = false;
        if (opened.IsNdefFormatted)
        {
            try
            {
                records.AddRange(opened.ReadNdef() ?? Enumerable.Empty<NdefRecord>());
            }
            catch (ContactlessException) { throw; }
            catch (Exception) { unreadable = true; } // data we can't decode is still "existing data"
        }

        return new NfcTagInfo(NfcUid.Normalize(connection.Card.UidHex), opened.ModelName, opened.UserMemoryBytes,
            opened.IsNdefFormatted, opened.IsReadOnly, records, unreadable);
    }
}
