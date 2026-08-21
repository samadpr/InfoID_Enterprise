namespace InfoID.Domain.Common.Enums;

public enum RfidCardTechnology
{
    MifareClassic, DESFire, HIDiCLASS, NFC
}

public enum RfidOperation
{
    Encode, Verify, BulkPreEncode
}

public enum RfidResult
{
    Success, Failed
}
