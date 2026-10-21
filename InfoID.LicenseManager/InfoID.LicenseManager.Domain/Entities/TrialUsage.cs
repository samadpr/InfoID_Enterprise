namespace InfoID.LicenseManager.Domain.Entities;

public static class TrialReservationStatus
{
    public const string Reserved = "Reserved";     // credits held while the app prints
    public const string Committed = "Committed";   // printed; CardsPrinted counted as used
    public const string Released = "Released";     // print failed or was cancelled
    public const string Expired = "Expired";       // the app never reported back (crash)
}

/// <summary>A customer account's trial printing allowance for one product. The server
/// is the only place the count lives, so reinstalling the app or deleting its data
/// can't reset it.</summary>
public class TrialUsage : EntityBase
{
    public long CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    /// <summary>Cards this account may print in trial mode (copied from the server
    /// setting when the row is created; staff can raise it for one account).</summary>
    public int CardLimit { get; set; }
    /// <summary>Cards actually printed.</summary>
    public int CardsUsed { get; set; }
    /// <summary>Cards held by print jobs in progress. Used + Reserved never exceeds the limit.</summary>
    public int CardsReserved { get; set; }
    public DateTime? FirstPrintDate { get; set; }
    public DateTime? LastPrintDate { get; set; }
}
