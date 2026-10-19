namespace InfoID.Domain.Common.Interfaces;

/// <summary>
/// The audit trail (FR-REP-4, FR-REP-7): an append-only, hash-chained record of what was
/// done in InfoID. Any layer records through this interface; the Infrastructure
/// implementation takes care of who/where/when and the hash chain.
/// Recording never throws -- a failure to audit must not break the action itself (it is
/// written to the technical log instead).
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
}

/// <summary>One audited action.</summary>
public sealed class AuditEntry
{
    public AuditEntry(string module, string action, string description)
    {
        Module = module;
        Action = action;
        Description = description;
    }

    /// <summary>Area of the app, see <see cref="AuditModules"/>.</summary>
    public string Module { get; }
    /// <summary>Short machine-readable action, e.g. "DesignSaved", "Printed".</summary>
    public string Action { get; }
    /// <summary>What happened, in words, e.g. "Design 'Staff ID' saved (version 3)".</summary>
    public string Description { get; }
    /// <summary>Affected table, e.g. "Template".</summary>
    public string? EntityName { get; init; }
    public long? EntityId { get; init; }
    /// <summary>Values before the change (serialized to JSON). Keep it to what matters --
    /// never secrets (codes, keys, passwords).</summary>
    public object? Before { get; init; }
    /// <summary>Values after the change (serialized to JSON).</summary>
    public object? After { get; init; }
}

/// <summary>Module names used in the audit log (one list, so filters stay consistent).</summary>
public static class AuditModules
{
    public const string Application = "Application";
    public const string Account = "Account";
    public const string Designs = "Designs";
    public const string Printing = "Printing";
    public const string Data = "Data";
    public const string License = "License";
    public const string Backup = "Backup";
    public const string Reports = "Reports";
    public const string Visitors = "Visitors";
    public const string Audit = "Audit";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Application, Account, Designs, Printing, Data, License, Backup, Reports, Visitors, Audit,
    };
}

/// <summary>Who is performing actions (Windows user, plus the signed-in InfoID account
/// when there is one). The Desktop app supplies its own implementation.</summary>
public interface IAuditActorProvider
{
    string CurrentActor { get; }
}
