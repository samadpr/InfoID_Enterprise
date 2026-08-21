namespace InfoID.Domain.Common.Enums;

public enum PrinterBrand
{
    Fargo, Evolis, Zebra, Magicard
}

public enum PrinterConnectionType
{
    USB, Network
}

public enum PrintJobType
{
    Single, Batch, Reprint
}

public enum PrintJobStatus
{
    Queued, Running, Paused, Completed, Cancelled, Failed
}

public enum PrintOutcome
{
    Success, Failed, Jam
}

public enum ReprintReasonCode
{
    Lost, Damaged, Renewed
}
