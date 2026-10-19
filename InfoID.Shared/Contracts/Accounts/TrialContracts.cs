namespace InfoID.Shared.Contracts.Accounts;

/// <summary>Trial printing allowance of the signed-in account (GET /api/v1/trial).</summary>
public class TrialStatusDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    /// <summary>Cards the account may print in trial mode.</summary>
    public int CardLimit { get; set; }
    /// <summary>Cards printed so far.</summary>
    public int CardsUsed { get; set; }
    /// <summary>Cards held by print jobs that are in progress right now.</summary>
    public int CardsReserved { get; set; }
    public int CardsRemaining { get; set; }
}

/// <summary>Reserve trial credits before printing (POST /api/v1/trial/reserve).</summary>
public class TrialReserveRequest
{
    public int Cards { get; set; }
    public string? DesignName { get; set; }
    public string? MachineFingerprint { get; set; }
    public string? AppVersion { get; set; }
}

public class TrialReserveResponse : TrialStatusDto
{
    /// <summary>Send back with commit (printed) or release (failed/cancelled).</summary>
    public Guid? ReservationId { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

/// <summary>Report the outcome of a reserved print job (POST /api/v1/trial/commit or /release).</summary>
public class TrialCompleteRequest
{
    public Guid ReservationId { get; set; }
    /// <summary>Commit only: cards actually printed (at most the reserved number).</summary>
    public int CardsPrinted { get; set; }
}
