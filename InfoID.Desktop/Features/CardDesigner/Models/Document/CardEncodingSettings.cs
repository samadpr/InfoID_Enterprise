using InfoID.CardEncoding.Nfc;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>
/// What a design writes to each card besides printing it: the NFC chip now, a magnetic
/// stripe later. Saved with the design (database metadata, .infoid export, versions,
/// duplicate). Designs saved before this existed have none, so NFC is off for them.
/// </summary>
public sealed class CardEncodingSettings
{
    public NfcEncodingSettings Nfc { get; set; } = new();
}

/// <summary>Which NDEF records an NFC tag gets. Stored in designs: append only.</summary>
public enum NfcContentMode
{
    /// <summary>One URI record (phones open it).</summary>
    Url,
    /// <summary>One Text record.</summary>
    Text,
    /// <summary>URI record followed by a Text record.</summary>
    UrlAndText,
}

/// <summary>NFC encoding of one design. Templates use the designer's {{Field}} syntax, so
/// every record gets its own content from its own data.</summary>
public sealed class NfcEncodingSettings
{
    public const string DefaultUrlTemplate = "https://infoidz.com/card/{{CardId}}";
    public const string DefaultTextTemplate = "{{CardId}}";

    public bool Enabled { get; set; }
    public NfcContentMode Mode { get; set; } = NfcContentMode.Url;
    public string UrlTemplate { get; set; } = DefaultUrlTemplate;
    public string TextTemplate { get; set; } = DefaultTextTemplate;

    /// <summary>Language code of the Text record (NFC Forum Text RTD). Not shown in the UI yet.</summary>
    public string TextLanguage { get; set; } = "en";

    /// <summary>Tag size the designer checks the content against. Only a design-time check:
    /// writing always checks the capacity of the actual tag on the reader.</summary>
    public NfcTagModel SizeCheckTag { get; set; } = NfcTagModel.Ntag216;

    /// <summary>Also write each tag's UID into a column of the data (Internal Database only).</summary>
    public bool SaveUidToData { get; set; }
    public string? UidColumn { get; set; }
}
