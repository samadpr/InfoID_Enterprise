namespace InfoID.CardEncoding.Contactless;

/// <summary>Contactless card families InfoID recognises from the PC/SC ATR. Cards whose
/// ATR doesn't say (DESFire, iCLASS, 125 kHz Prox on some readers) are Unknown.</summary>
public enum ContactlessCardType
{
    Unknown,
    MifareClassic1K,
    MifareClassic4K,
    MifareMini,
    /// <summary>MIFARE Ultralight family, including NTAG213/215/216 (NFC Forum Type 2).</summary>
    MifareUltralightOrNtag,
    MifareUltralightC,
}

/// <summary>The card currently on the reader.</summary>
public sealed record ContactlessCard(string ReaderName, byte[] Atr, byte[] Uid, ContactlessCardType Type)
{
    public string UidHex => Convert.ToHexString(Uid);
    public string AtrHex => Convert.ToHexString(Atr);

    /// <summary>True for NFC Forum Type 2 tags InfoID can write NDEF to.</summary>
    public bool IsNfcType2 => Type is ContactlessCardType.MifareUltralightOrNtag or ContactlessCardType.MifareUltralightC;
}

/// <summary>A card was placed on / removed from the watched reader.</summary>
public sealed class CardPresenceEventArgs : EventArgs
{
    public CardPresenceEventArgs(string readerName, byte[] atr)
    {
        ReaderName = readerName;
        Atr = atr;
    }

    public string ReaderName { get; }
    /// <summary>The card's ATR (empty when the card was removed).</summary>
    public byte[] Atr { get; }
}

public enum ContactlessError
{
    NoReader,
    ReaderUnavailable,
    ReaderBusy,
    NoCard,
    CardRemoved,
    CommandFailed,
    NotSupported,
    NotFormatted,
    ReadOnly,
    NotEnoughSpace,
    VerifyFailed,
}

/// <summary>Every reader/card problem, with a message that can be shown to the user as-is.</summary>
public sealed class ContactlessException : Exception
{
    public ContactlessException(ContactlessError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;

    public ContactlessError Error { get; }
}
