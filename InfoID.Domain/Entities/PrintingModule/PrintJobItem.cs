using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.IssuanceModule;

namespace InfoID.Domain.Entities.PrintingModule;

/// <summary>
/// One card's outcome within a print job, including reprint reason codes (FR-PRN-4, FR-PRN-6).
/// </summary>
public class PrintJobItem : BaseEntity
{
    public long PrintJobId { get; set; }
    public PrintJob? PrintJob { get; set; }
    public long CardId { get; set; }
    public Card? Card { get; set; }
    public int Sequence { get; set; }  // Order within the batch
    public PrintOutcome Outcome { get; set; }  // Success / Failed / Jam
    public ReprintReasonCode? ReprintReasonCode { get; set; }  // Lost / Damaged / Renewed
    public DateTime? PrintedDate { get; set; }
}
