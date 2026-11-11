using InfoID.CardEncoding.Contactless;

namespace InfoID.CardEncoding.Nfc;

/// <summary>Reads and writes NDEF on an NFC Forum Type 2 tag (NTAG213/215/216, MIFARE
/// Ultralight) through PC/SC Read Binary / Update Binary. Writes only user memory (page 4
/// onwards) -- never the UID, lock bytes or configuration pages -- and never locks the tag.</summary>
public sealed class Type2Tag
{
    private const int FirstUserPage = 4;
    private readonly ICardConnection _connection;
    private readonly byte[] _capabilityContainer;

    private Type2Tag(ICardConnection connection, byte[] capabilityContainer)
    {
        _connection = connection;
        _capabilityContainer = capabilityContainer;
    }

    /// <summary>Reads the tag's capability container. Throws NotSupported for other card types.</summary>
    public static Type2Tag Open(ICardConnection connection)
    {
        if (!connection.Card.IsNfcType2 && connection.Card.Type != ContactlessCardType.Unknown)
            throw new ContactlessException(ContactlessError.NotSupported,
                $"This card ({connection.Card.Type}) is not an NFC tag (NTAG / MIFARE Ultralight).");

        var head = ReadFourPages(connection, 0);               // pages 0-3; page 3 = capability container
        return new Type2Tag(connection, head.AsSpan(12, 4).ToArray());
    }

    /// <summary>True when the card is an NFC Forum Type 2 tag (NTAG / Ultralight): known from
    /// its ATR, or -- for readers that report a card name InfoID doesn't list (e.g. the
    /// OMNIKEY 5422 reports NTAG as 00 3D) -- detected by reading the capability container:
    /// page 3 starting with the NDEF marker E1. Other cards return false.</summary>
    public static bool TryOpen(ICardConnection connection, out Type2Tag? tag)
    {
        tag = null;

        if (connection.Card.IsNfcType2)
        {
            tag = Open(connection);
            return true;
        }

        if (connection.Card.Type != ContactlessCardType.Unknown)
            return false;                       // e.g. MIFARE Classic: not a Type 2 tag

        try
        {
            var candidate = Open(connection);
            if (!candidate.IsNdefFormatted) return false;
            tag = candidate;
            return true;
        }
        catch (ContactlessException ex) when (ex.Error == ContactlessError.CommandFailed)
        {
            return false;                       // the card doesn't answer Type 2 reads
        }
    }

    public ContactlessCard Card => _connection.Card;
    public bool IsNdefFormatted => _capabilityContainer[0] == 0xE1;
    public bool IsReadOnly => (_capabilityContainer[3] & 0x0F) != 0x00;
    public int UserMemoryBytes => _capabilityContainer[2] * 8;

    public string ModelName => _capabilityContainer[2] switch
    {
        0x06 => "MIFARE Ultralight",
        0x12 => "NTAG213",
        0x3E => "NTAG215",
        0x6D => "NTAG216",
        _ => $"NFC tag ({UserMemoryBytes} bytes)",
    };

    public byte[] ReadUserMemory()
    {
        var memory = new byte[UserMemoryBytes];
        for (var offset = 0; offset < memory.Length; offset += 16)
        {
            var chunk = ReadFourPages(_connection, FirstUserPage + offset / 4);
            Array.Copy(chunk, 0, memory, offset, Math.Min(16, memory.Length - offset));
        }
        return memory;
    }

    public IReadOnlyList<NdefRecord> ReadNdef() => NdefMessage.DecodeFromType2Memory(ReadUserMemory());

    /// <summary>Writes the records, then reads them back and compares (write-then-verify).
    /// Open the connection with exclusive = true.</summary>
    public void WriteNdef(IReadOnlyList<NdefRecord> records)
    {
        if (!IsNdefFormatted)
            throw new ContactlessException(ContactlessError.NotFormatted, "This tag isn't formatted for NFC data (NDEF).");
        if (IsReadOnly)
            throw new ContactlessException(ContactlessError.ReadOnly, "This tag is locked (read-only) and can't be written.");

        var tlv = NdefMessage.ToType2Tlv(NdefMessage.Encode(records));
        var data = new byte[(tlv.Length + 3) / 4 * 4];
        Array.Copy(tlv, data, tlv.Length);

        if (data.Length > UserMemoryBytes)
            throw new ContactlessException(ContactlessError.NotEnoughSpace,
                $"The data needs {data.Length} bytes but this {ModelName} holds {UserMemoryBytes}.");

        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var page = (byte)(FirstUserPage + offset / 4);
            var apdu = new byte[] { 0xFF, 0xD6, 0x00, page, 0x04 }.Concat(data.AsSpan(offset, 4).ToArray()).ToArray();
            _connection.Transmit(apdu);                         // PC/SC Update Binary: one page
        }

        var readBack = ReadUserMemory();
        if (!readBack.AsSpan(0, data.Length).SequenceEqual(data))
            throw new ContactlessException(ContactlessError.VerifyFailed,
                "The data read back from the card doesn't match what was written. Place the card again and retry.");
    }

    private static byte[] ReadFourPages(ICardConnection connection, int page)
    {
        var data = connection.Transmit(new byte[] { 0xFF, 0xB0, 0x00, (byte)page, 0x10 }); // Read Binary: 16 bytes
        if (data.Length < 16)
            throw new ContactlessException(ContactlessError.CommandFailed, "The card returned too little data.");
        return data;
    }
}
