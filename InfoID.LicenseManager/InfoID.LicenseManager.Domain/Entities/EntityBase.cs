namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>Common base fields (Domain Model section 1). Dates are UTC (PostgreSQL
/// timestamptz); CreatedDate/ModifiedDate/By are set automatically by LicenseDbContext.</summary>
public abstract class EntityBase
{
    public long Id { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public bool Cancelled { get; set; }
}
