namespace InfoID.CardEncoding.Contactless;

/// <summary>Reads the card family from a PC/SC Part 3 contactless ATR:
/// 3B 8n 80 01 80 4F 0C A0 00 00 03 06 [standard] [card name hi] [card name lo] ...</summary>
public static class AtrParser
{
    public static ContactlessCardType GetCardType(byte[] atr)
    {
        if (atr.Length < 15 || atr[4] != 0x80 || atr[5] != 0x4F ||
            atr[7] != 0xA0 || atr[8] != 0x00 || atr[9] != 0x00 || atr[10] != 0x03 || atr[11] != 0x06)
            return ContactlessCardType.Unknown;

        var cardName = (atr[13] << 8) | atr[14];
        return cardName switch
        {
            0x0001 => ContactlessCardType.MifareClassic1K,
            0x0002 => ContactlessCardType.MifareClassic4K,
            0x0003 => ContactlessCardType.MifareUltralightOrNtag,
            0x0026 => ContactlessCardType.MifareMini,
            0x003A => ContactlessCardType.MifareUltralightC,
            _ => ContactlessCardType.Unknown,
        };
    }
}
