namespace InfoID.LicenseManager.Application.DTOs.Trial;

/// <summary>Trial printing settings (the "Trial" section of the configuration).</summary>
public sealed class TrialOptions
{
    /// <summary>Cards a new account may print in trial mode.</summary>
    public int CardLimit { get; set; } = 10;

    /// <summary>How long a reservation holds credits if the app never reports back.</summary>
    public int ReservationMinutes { get; set; } = 30;

    /// <summary>Largest single trial print job (also bounded by the remaining credits).</summary>
    public int MaxCardsPerReservation { get; set; } = 10;
}

public sealed record TrialStatusDto(int Limit, int Used, int Reserved)
{
    public int Remaining => Math.Max(0, Limit - Used - Reserved);
}

public sealed record TrialReserveResultDto(bool Succeeded, Guid? ReservationKey, TrialStatusDto Status, string? Error, DateTime? ExpiresUtc = null);

public sealed record TrialCompleteResultDto(bool Succeeded, TrialStatusDto Status, string? Error);
