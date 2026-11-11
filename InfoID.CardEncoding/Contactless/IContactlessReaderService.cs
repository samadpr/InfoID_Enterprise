namespace InfoID.CardEncoding.Contactless;

/// <summary>An open connection to the card on a reader. Dispose to release it.</summary>
public interface ICardConnection : IDisposable
{
    ContactlessCard Card { get; }

    /// <summary>Sends one APDU and returns the response data (without SW1/SW2).
    /// Throws ContactlessException unless the card answers 90 00.</summary>
    byte[] Transmit(byte[] apdu);
}

/// <summary>Contactless (13.56 MHz) reader access for InfoID: list readers, watch one for
/// cards, connect to the card. Calls block, so callers run them off the UI thread; the
/// events are raised on a background thread.</summary>
public interface IContactlessReaderService : IDisposable
{
    /// <summary>Names of the PC/SC readers connected now (empty when none, or the Windows
    /// Smart Card service isn't running).</summary>
    IReadOnlyList<string> GetReaders();

    /// <summary>The reader being watched, or null.</summary>
    string? SelectedReader { get; }

    /// <summary>Watch this reader for cards (stops watching the previous one); null stops watching.</summary>
    void SelectReader(string? readerName);

    event EventHandler<CardPresenceEventArgs>? CardInserted;
    event EventHandler<CardPresenceEventArgs>? CardRemoved;

    /// <summary>The watcher stopped, e.g. the reader was unplugged.</summary>
    event EventHandler<ContactlessException>? ReaderError;

    /// <summary>Connects to the card on the selected reader. <paramref name="exclusive"/>:
    /// no other program may use the card until the connection is disposed -- use it while writing.</summary>
    ICardConnection Connect(bool exclusive = false);
}
