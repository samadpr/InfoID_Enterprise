namespace InfoID.CardEncoding.Nfc;

/// <summary>NFC Forum Type 2 tag models InfoID supports. Stored in designs (size check),
/// so values are only ever appended, never reordered.</summary>
public enum NfcTagModel
{
    Ntag213,
    Ntag215,
    Ntag216,
    MifareUltralight,
}

public static class NfcTagModels
{
    public static IReadOnlyList<NfcTagModel> All { get; } =
        new[] { NfcTagModel.Ntag213, NfcTagModel.Ntag215, NfcTagModel.Ntag216, NfcTagModel.MifareUltralight };

    /// <summary>Bytes available for NDEF data: the user memory the tag announces in its
    /// capability container (NTAG213 144, NTAG215 496, NTAG216 872, Ultralight 48).</summary>
    public static int CapacityBytes(NfcTagModel model) => model switch
    {
        NfcTagModel.Ntag213 => 144,
        NfcTagModel.Ntag215 => 496,
        NfcTagModel.Ntag216 => 872,
        NfcTagModel.MifareUltralight => 48,
        _ => 0,
    };

    public static string DisplayName(NfcTagModel model) => model switch
    {
        NfcTagModel.Ntag213 => "NTAG213",
        NfcTagModel.Ntag215 => "NTAG215",
        NfcTagModel.Ntag216 => "NTAG216",
        NfcTagModel.MifareUltralight => "MIFARE Ultralight",
        _ => model.ToString(),
    };
}
