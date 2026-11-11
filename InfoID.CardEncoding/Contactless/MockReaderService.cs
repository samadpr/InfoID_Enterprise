namespace InfoID.CardEncoding.Contactless;

/// <summary>In-memory reader with a simulated NFC Type 2 tag, for unit tests and for
/// developing the screens without hardware.</summary>
public sealed class MockReaderService : IContactlessReaderService
{
    public const string MockReaderName = "InfoID Mock Reader";
    private MockType2Tag? _card;

    public string? SelectedReader { get; private set; }
    public event EventHandler<CardPresenceEventArgs>? CardInserted;
    public event EventHandler<CardPresenceEventArgs>? CardRemoved;
    public event EventHandler<ContactlessException>? ReaderError;

    public IReadOnlyList<string> GetReaders() => new[] { MockReaderName };
    public void SelectReader(string? readerName) => SelectedReader = readerName;

    public void PlaceCard(MockType2Tag card)
    {
        _card = card;
        CardInserted?.Invoke(this, new CardPresenceEventArgs(MockReaderName, card.Atr));
    }

    public void RemoveCard()
    {
        _card = null;
        CardRemoved?.Invoke(this, new CardPresenceEventArgs(MockReaderName, Array.Empty<byte>()));
    }

    /// <summary>Simulates the reader being unplugged.</summary>
    public void RaiseReaderError() =>
        ReaderError?.Invoke(this, new ContactlessException(ContactlessError.ReaderUnavailable, "Mock reader unplugged."));

    public ICardConnection Connect(bool exclusive = false)
    {
        if (SelectedReader is null)
            throw new ContactlessException(ContactlessError.NoReader, "No card reader is selected.");
        if (_card is null)
            throw new ContactlessException(ContactlessError.NoCard, "No card on the reader.");
        return new MockConnection(_card);
    }

    public void Dispose() { }

    private sealed class MockConnection : ICardConnection
    {
        private readonly MockType2Tag _tag;
        public MockConnection(MockType2Tag tag)
        {
            _tag = tag;
            Card = new ContactlessCard(MockReaderName, tag.Atr, tag.Uid, ContactlessCardType.MifareUltralightOrNtag);
        }

        public ContactlessCard Card { get; }
        public byte[] Transmit(byte[] apdu) => _tag.Execute(apdu);
        public void Dispose() { }
    }
}

/// <summary>A simulated NTAG: 4 header pages, user memory, then 5 configuration pages.</summary>
public sealed class MockType2Tag
{
    private readonly byte[] _memory;
    private readonly int _userBytes;

    public MockType2Tag(int userBytes = 496, bool readOnly = false, bool formatted = true)
    {
        _userBytes = userBytes;
        _memory = new byte[(4 + userBytes / 4 + 5) * 4];
        Uid = new byte[] { 0x04, 0xD5, 0x7C, 0xB1, 0xAF, 0x61, 0x80 };
        Array.Copy(Uid, _memory, 3);
        Array.Copy(Uid, 3, _memory, 4, 4);
        _memory[12] = formatted ? (byte)0xE1 : (byte)0x00;   // capability container (page 3)
        _memory[13] = 0x10;
        _memory[14] = (byte)(userBytes / 8);
        _memory[15] = readOnly ? (byte)0x0F : (byte)0x00;
        _memory[16] = 0x03; _memory[17] = 0x00; _memory[18] = 0xFE; // empty NDEF TLV
    }

    public byte[] Uid { get; }
    public byte[] Atr { get; } = Convert.FromHexString("3B8F8001804F0CA0000003060300030000000068");

    /// <summary>When true, the next write fails as if the card was removed mid-write.</summary>
    public bool FailNextWrite { get; set; }

    public byte[] Execute(byte[] apdu)
    {
        if (apdu.Length >= 5 && apdu[0] == 0xFF && apdu[1] == 0xCA) return Uid;

        if (apdu.Length >= 5 && apdu[0] == 0xFF && apdu[1] == 0xB0)
        {
            var start = apdu[3] * 4;
            var result = new byte[16];
            for (var i = 0; i < 16; i++) result[i] = _memory[(start + i) % _memory.Length];
            return result;
        }

        if (apdu.Length >= 9 && apdu[0] == 0xFF && apdu[1] == 0xD6)
        {
            var page = apdu[3];
            var lastUserPage = 4 + _userBytes / 4 - 1;
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new ContactlessException(ContactlessError.CardRemoved, "The card moved or was removed.");
            }
            if (page < 4 || page > lastUserPage || _memory[15] != 0x00)
                throw new ContactlessException(ContactlessError.CommandFailed, "The card refused the command (status 6300).");
            Array.Copy(apdu, 5, _memory, page * 4, 4);
            return Array.Empty<byte>();
        }

        throw new ContactlessException(ContactlessError.CommandFailed, "The card refused the command (status 6D00).");
    }
}
