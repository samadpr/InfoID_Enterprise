using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.PrintingModule;

/// <summary>One printed record in a job to be recorded (FR-PRN-6).</summary>
public class PrintedRecordInput
{
    /// <summary>Value of the Person ID column (e.g. EmployeeNo). Null when no ID column
    /// is chosen: the card is recorded, but reprints can't be recognized.</summary>
    public string? PersonKey { get; set; }

    /// <summary>Name shown in history (from a name column, the ID, or the record label).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Card expiry for this record (validity setting or expiry column); null = no expiry.</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>True when ExpiryDate came from the record's own data (an expiry column),
    /// i.e. it is what the printed card shows. Then it is always kept. Otherwise a Lost or
    /// Damaged replacement keeps the replaced card's original expiry.</summary>
    public bool ExpiryFromData { get; set; }
}

/// <summary>Everything needed to record one print run.</summary>
public class RecordPrintJobRequest
{
    public string DesignName { get; set; } = string.Empty;
    /// <summary>Saved design (Template row); null for unsaved or file-based designs.</summary>
    public long? TemplateId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    /// <summary>False for a design without database data (static cards): recorded as
    /// generic cards, never matched as reprints.</summary>
    public bool IsDataBound { get; set; }
    public int CopiesPerRecord { get; set; } = 1;
    public bool Succeeded { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime StartedUtc { get; set; }
    /// <summary>Reason used for records that already had a card (null = treat as new cards).</summary>
    public ReprintReasonCode? ReprintReason { get; set; }
    public string? ReprintNote { get; set; }
    public List<PrintedRecordInput> Records { get; set; } = new();
}

public class RecordPrintJobResult
{
    public long PrintJobId { get; set; }
    public int NewCards { get; set; }
    public int Reprints { get; set; }
    /// <summary>Card numbers issued by this run, in print order.</summary>
    public List<string> CardNumbers { get; } = new();
}

/// <summary>A card already issued to one of the people about to be printed.</summary>
public class ExistingCardDto
{
    public string PersonKey { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public DateTime? IssuedDate { get; set; }
}

public class PrintJobFilter
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    /// <summary>Matches design name, printer, or a cardholder name / ID in the job.</summary>
    public string? SearchText { get; set; }
    public int MaxResults { get; set; } = 500;
}

public class PrintJobSummaryDto
{
    public long Id { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string DesignName { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public PrintJobType JobType { get; set; }
    public PrintJobStatus Status { get; set; }
    public int Records { get; set; }
    public int TotalCards { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int ReprintCount { get; set; }
}

public class PrintJobItemDto
{
    public int Sequence { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PersonKey { get; set; }
    public PrintOutcome Outcome { get; set; }
    public ReprintReasonCode? ReprintReason { get; set; }
    public CardStatus CardStatus { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
