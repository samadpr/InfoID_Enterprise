using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Exceptions;

namespace InfoID.LicenseManager.Domain.Entities;

public static class LicenseCodeStatus
{
    public const string Available = "Available";   // created, never activated
    public const string Active = "Active";         // at least one active machine
    public const string Revoked = "Revoked";
}

/// <summary>Module N LicenseCode (FR-ADM-4) + fields the SRS needs that the table
/// lacked: status, fixed expiry, limit period, activation slots, batch id.</summary>
public class LicenseCode : EntityBase
{
    public const int MaxMachinesPerCode = 100;

    /// <summary>Normalized 16-character code (no dashes). Unique.</summary>
    public string ActivationCode { get; set; } = "";
    public string Tier { get; set; } = "Standard";
    public string LicenseType { get; set; } = "Subscription";
    public int? MaxCardsPerPeriod { get; set; }
    /// <summary>Added: Monthly / Yearly.</summary>
    public string CardLimitPeriod { get; set; } = "Monthly";
    /// <summary>Added: subscription end date (null for perpetual).</summary>
    public DateOnly? ExpiryDate { get; set; }
    /// <summary>Added: how many machines may be active at once. Default 1.</summary>
    public int MaxActivations { get; set; } = 1;
    /// <summary>Added: Available / Active / Revoked.</summary>
    public string Status { get; set; } = LicenseCodeStatus.Available;
    /// <summary>Added: whether this code may be activated WITHOUT the online server
    /// (phone/QR page or manual request code). Off by default: most licenses activate
    /// online only, the more secure path. Staff can always issue a key manually.</summary>
    public bool AllowOfflineActivation { get; set; }
    /// <summary>Added: groups codes created together (FR-ADM-4 batch).</summary>
    public Guid? BatchId { get; set; }
    public DateTime? RevokedDate { get; set; }
    public long? CustomerRecordId { get; set; }
    public CustomerRecord? CustomerRecord { get; set; }
    public bool IsUsed { get; set; }
    public long? CreatedByAdminUserId { get; set; }
    public AdminUser? CreatedByAdminUser { get; set; }
    public string? Notes { get; set; }
    /// <summary>Added: which product this code licenses (multi-product platform).</summary>
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    /// <summary>Added: the customer account this code belongs to ("My licenses" in the
    /// app). Set when a license request is approved; optional for codes sold directly.</summary>
    public long? CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }

    public List<ActivationRecord> Activations { get; set; } = new();

    // ------------------------------------------------------------------ behaviour ----

    /// <summary>Machines currently holding this code.</summary>
    public IEnumerable<ActivationRecord> ActiveActivations =>
        Activations.Where(a => a.Status == ActivationStatus.Active && !a.Cancelled);

    public bool IsRevoked => Status == LicenseCodeStatus.Revoked;

    /// <summary>Creates a new, never-activated code. Enforces every invariant of a license definition.</summary>
    /// <param name="activationCode">Normalized 16-character code (no dashes).</param>
    /// <param name="today">Current UTC date, so "expiry must be in the future" is testable.</param>
    public static LicenseCode Create(
        string activationCode,
        LicenseTier tier,
        LicenseType type,
        DateOnly? expiryDate,
        int? maxCardsPerPeriod,
        CardLimitPeriod limitPeriod,
        int maxActivations,
        bool allowOfflineActivation,
        string productCode,
        DateOnly today)
    {
        var termsProblem = new SerialKeyTerms(tier, type, expiryDate, maxCardsPerPeriod, limitPeriod).Validate();
        if (termsProblem is not null) throw new DomainRuleException(termsProblem);
        if (expiryDate is { } expiry && expiry <= today)
            throw new DomainRuleException("The expiry date must be in the future.");
        if (maxActivations is < 1 or > MaxMachinesPerCode)
            throw new DomainRuleException($"Machines per code must be between 1 and {MaxMachinesPerCode}.");

        return new LicenseCode
        {
            ActivationCode = activationCode,
            Tier = tier.ToString(),
            LicenseType = type.ToString(),
            ExpiryDate = expiryDate,
            MaxCardsPerPeriod = maxCardsPerPeriod,
            CardLimitPeriod = limitPeriod.ToString(),
            MaxActivations = maxActivations,
            AllowOfflineActivation = allowOfflineActivation,
            ProductCode = productCode,
            Status = LicenseCodeStatus.Available,
        };
    }

    /// <summary>The license terms a serial key or signed token carries for this code. A tier or expiry
    /// override (staff, at generation time) wins over the code's own value.</summary>
    public SerialKeyTerms ToTerms(LicenseTier? tierOverride = null, DateOnly? expiryOverride = null) => new(
        tierOverride ?? Enum.Parse<LicenseTier>(Tier),
        Enum.Parse<LicenseType>(LicenseType),
        expiryOverride ?? ExpiryDate,
        MaxCardsPerPeriod,
        Enum.Parse<CardLimitPeriod>(CardLimitPeriod));

    /// <summary>Revoking ends every machine's activation; each machine stops at its next online check-in.</summary>
    public void Revoke(DateTime utcNow)
    {
        Status = LicenseCodeStatus.Revoked;
        RevokedDate = utcNow;
        foreach (var activation in Activations.Where(a => a.Status == ActivationStatus.Active))
        {
            activation.Status = ActivationStatus.Deactivated;
            activation.DeactivatedDate = utcNow;
        }
    }

    public void Extend(DateOnly newExpiry, DateOnly today)
    {
        if (newExpiry <= today)
            throw new DomainRuleException("The new expiry date must be in the future.");
        if (LicenseType == nameof(Licensing.Core.LicenseType.Perpetual))
            throw new DomainRuleException("Perpetual licenses have no expiry date.");

        ExpiryDate = newExpiry;
    }

    public void SetAllowOfflineActivation(bool allow) => AllowOfflineActivation = allow;

    /// <summary>Frees every machine slot (customer deactivated offline, or the old PC died). Returns one
    /// transfer-history record per freed machine for the caller to store. A code with no machines in
    /// use goes back to Available; a revoked code stays revoked.</summary>
    public IReadOnlyList<TransferHistory> FreeMachineSlots(DateTime utcNow, string reason)
    {
        var freed = ActiveActivations.ToList();
        var history = new List<TransferHistory>(freed.Count);
        foreach (var activation in freed)
        {
            activation.Status = ActivationStatus.Deactivated;
            activation.DeactivatedDate = utcNow;
            history.Add(new TransferHistory
            {
                LicenseCode = this,
                OldActivationRecord = activation,
                TransferDate = utcNow,
                Reason = reason,
            });
        }

        if (Status == LicenseCodeStatus.Active && freed.Count > 0)
            Status = LicenseCodeStatus.Available;
        return history;
    }

    /// <summary>
    /// Registers a new activation on this code. Re-activating a machine that already holds the code
    /// (reinstall, renewal) replaces its record instead of using another slot. Callers must have
    /// checked <see cref="Rules.ActivationRules"/> while holding the row lock.
    /// </summary>
    public void RegisterActivation(ActivationRecord activation, DateTime utcNow)
    {
        foreach (var previous in Activations.Where(a => a.Status == ActivationStatus.Active && a.MachineFingerprint == activation.MachineFingerprint))
        {
            previous.Status = ActivationStatus.Deactivated;
            previous.DeactivatedDate = utcNow;
        }

        Activations.Add(activation);
        IsUsed = true;
        Status = LicenseCodeStatus.Active;
    }

    /// <summary>The newest active record of a machine, or null when that machine does not hold this code.</summary>
    public ActivationRecord? FindActiveActivation(string fingerprintHex) =>
        ActiveActivations.Where(a => a.MachineFingerprint == fingerprintHex).OrderByDescending(a => a.Id).FirstOrDefault();

    /// <summary>
    /// The terms an online machine gets at check-in: the code's CURRENT values (so an extension made by staff
    /// reaches the PC), keeping any edition override given when the machine was activated and never going
    /// back to an earlier expiry than the one already issued. Perpetual licenses never expire.
    /// </summary>
    public SerialKeyTerms CheckInTerms(ActivationRecord activation)
    {
        var tier = Enum.Parse<LicenseTier>(activation.IssuedTier);
        var expiry = Licensing.Core.LicenseType.Perpetual.ToString() == LicenseType ? null
            : ExpiryDate is null ? activation.IssuedExpiryDate
            : activation.IssuedExpiryDate is null ? ExpiryDate
            : ExpiryDate > activation.IssuedExpiryDate ? ExpiryDate : activation.IssuedExpiryDate;
        return ToTerms(tier, expiry);
    }

    /// <summary>The customer deactivated a machine from InfoID (transfer to a new PC): frees its slot. Returns the
    /// transfer-history records; a code left with no machine in use goes back to Available (never a revoked one).</summary>
    public IReadOnlyList<TransferHistory> DeactivateMachine(string fingerprintHex, DateTime utcNow, string reason)
    {
        var freed = ActiveActivations.Where(a => a.MachineFingerprint == fingerprintHex).ToList();
        var history = new List<TransferHistory>(freed.Count);
        foreach (var activation in freed)
        {
            activation.Status = ActivationStatus.Deactivated;
            activation.DeactivatedDate = utcNow;
            history.Add(new TransferHistory { LicenseCode = this, OldActivationRecord = activation, TransferDate = utcNow, Reason = reason });
        }

        if (Status == LicenseCodeStatus.Active && !ActiveActivations.Any())
            Status = LicenseCodeStatus.Available;
        return history;
    }
}
