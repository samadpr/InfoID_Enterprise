using PCSC;
using PCSC.Exceptions;
using PCSC.Monitoring;

namespace InfoID.CardEncoding.Contactless;

/// <summary>IContactlessReaderService over Windows PC/SC (WinSCard) via PCSC-sharp. Works
/// with any CCID contactless reader, e.g. HID OMNIKEY 5427 CK ("HID OMNIKEY 5427 CK CL 0").</summary>
public sealed class PcscReaderService : IContactlessReaderService
{
    private readonly object _gate = new();
    private ISCardMonitor? _monitor;

    public string? SelectedReader { get; private set; }

    public event EventHandler<CardPresenceEventArgs>? CardInserted;
    public event EventHandler<CardPresenceEventArgs>? CardRemoved;
    public event EventHandler<ContactlessException>? ReaderError;

    public IReadOnlyList<string> GetReaders()
    {
        try
        {
            using var context = ContextFactory.Instance.Establish(SCardScope.System);
            return context.GetReaders() ?? Array.Empty<string>();
        }
        catch (PCSCException)
        {
            return Array.Empty<string>(); // no readers, or the Smart Card service is stopped
        }
    }

    public void SelectReader(string? readerName)
    {
        lock (_gate)
        {
            StopMonitor();
            SelectedReader = string.IsNullOrWhiteSpace(readerName) ? null : readerName;
            if (SelectedReader is null) return;

            try
            {
                var monitor = MonitorFactory.Instance.Create(SCardScope.System);
                monitor.CardInserted += (_, e) =>
                    CardInserted?.Invoke(this, new CardPresenceEventArgs(e.ReaderName, e.Atr ?? Array.Empty<byte>()));
                monitor.CardRemoved += (_, e) =>
                    CardRemoved?.Invoke(this, new CardPresenceEventArgs(e.ReaderName, Array.Empty<byte>()));
                monitor.MonitorException += (_, ex) =>
                    ReaderError?.Invoke(this, new ContactlessException(ContactlessError.ReaderUnavailable,
                        "The card reader stopped responding. Check that it is connected.", ex));
                monitor.Start(SelectedReader);
                _monitor = monitor;
            }
            catch (PCSCException ex)
            {
                throw Map(ex);
            }
        }
    }

    public ICardConnection Connect(bool exclusive = false)
    {
        var readerName = SelectedReader
            ?? throw new ContactlessException(ContactlessError.NoReader,
                "No card reader is selected. Choose one in Settings → RFID / NFC.");

        ISCardContext? context = null;
        ICardReader? reader = null;
        try
        {
            context = ContextFactory.Instance.Establish(SCardScope.System);
            reader = context.ConnectReader(readerName,
                exclusive ? SCardShareMode.Exclusive : SCardShareMode.Shared, SCardProtocol.Any);
            return new PcscCardConnection(context, reader, readerName);
        }

        catch (PCSCException ex)
        {
            reader?.Dispose();
            context?.Dispose();

            // While CONNECTING, readers such as the OMNIKEY report "card removed" (not
            // "no card") when nothing is on the reader: treat both as "no card yet".
            if (ex.SCardError is SCardError.NoSmartcard or SCardError.RemovedCard or SCardError.ResetCard
              or SCardError.UnresponsiveCard or SCardError.UnpoweredCard)
                throw new ContactlessException(ContactlessError.NoCard,
                    "No card on the reader. Place a card flat on the reader.", ex);

            throw Map(ex);
        }
        catch
        {
            reader?.Dispose();
            context?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_gate) StopMonitor();
    }

    private void StopMonitor()
    {
        if (_monitor is null) return;
        try { _monitor.Cancel(); } catch (PCSCException) { /* already stopped */ }
        _monitor.Dispose();
        _monitor = null;
    }

    /// <summary>PC/SC errors as plain-language ContactlessExceptions.</summary>
    internal static ContactlessException Map(PCSCException ex) => ex.SCardError switch
    {
        SCardError.NoSmartcard =>
            new(ContactlessError.NoCard, "No card on the reader. Place a card flat on the reader.", ex),
        SCardError.RemovedCard or SCardError.ResetCard or SCardError.UnresponsiveCard or SCardError.UnpoweredCard =>
            new(ContactlessError.CardRemoved, "The card moved or was removed. Place it again and keep it still.", ex),
        SCardError.SharingViolation =>
            new(ContactlessError.ReaderBusy, "Another program is using the reader (for example HID Workbench). Close it and try again.", ex),
        SCardError.ReaderUnavailable or SCardError.UnknownReader or SCardError.NoReadersAvailable or SCardError.NoService =>
            new(ContactlessError.ReaderUnavailable, "The card reader isn't available. Check that it is connected.", ex),
        _ => new(ContactlessError.CommandFailed, $"Reader error: {ex.Message}", ex),
    };
}

/// <summary>One PC/SC card connection: reads the ATR and UID when opened.</summary>
internal sealed class PcscCardConnection : ICardConnection
{
    private readonly ISCardContext _context;
    private readonly ICardReader _reader;

    public PcscCardConnection(ISCardContext context, ICardReader reader, string readerName)
    {
        _context = context;
        _reader = reader;

        byte[] atr;
        try { atr = reader.GetAttrib(SCardAttribute.AtrString) ?? Array.Empty<byte>(); }
        catch (PCSCException) { atr = Array.Empty<byte>(); }

        var uid = Transmit(new byte[] { 0xFF, 0xCA, 0x00, 0x00, 0x00 }); // PC/SC "Get Data": UID
        Card = new ContactlessCard(readerName, atr, uid, AtrParser.GetCardType(atr));
    }

    public ContactlessCard Card { get; }

    public byte[] Transmit(byte[] apdu)
    {
        var buffer = new byte[258];
        int length;
        try
        {
            length = _reader.Transmit(apdu, buffer);
        }
        catch (PCSCException ex)
        {
            throw PcscReaderService.Map(ex);
        }

        if (length < 2)
            throw new ContactlessException(ContactlessError.CommandFailed, "The card did not answer.");

        var sw1 = buffer[length - 2];
        var sw2 = buffer[length - 1];
        if (sw1 != 0x90 || sw2 != 0x00)
            throw new ContactlessException(ContactlessError.CommandFailed,
                $"The card refused the command (status {sw1:X2}{sw2:X2}).");

        return buffer.AsSpan(0, length - 2).ToArray();
    }

    public void Dispose()
    {
        _reader.Dispose();
        _context.Dispose();
    }
}
