namespace InfoID.Domain.Common;

/// <summary>
/// Shared base fields applied to every entity in InfoID, per Domain Model v1.1
/// ("Common Base Fields" section). Every table inherits: Id, CreatedDate, ModifiedDate,
/// CreatedBy, ModifiedBy, Cancelled (soft-delete flag).
/// </summary>
public abstract class BaseEntity
{
    /// <summary>Surrogate primary key. bigint identity(1,1) in the database.</summary>
    public long Id { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? ModifiedDate { get; set; }

    /// <summary>Free-text or AppUserId reference of who created the row (kept as string
    /// per the doc's audit pattern; adjust to a hard FK if you'd rather enforce it).</summary>
    public string? CreatedBy { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Soft-delete flag. Rows are never hard-deleted from business tables —
    /// set Cancelled = true instead so history/audit stays intact.
    /// </summary>
    public bool Cancelled { get; set; } = false;
}
