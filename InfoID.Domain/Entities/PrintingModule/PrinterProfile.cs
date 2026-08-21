using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.PrintingModule;

/// <summary>
/// A configured physical printer, with brand-specific capability flags.
/// </summary>
public class PrinterProfile : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public PrinterBrand Brand { get; set; }  // Fargo / Evolis / Zebra / Magicard
    public string? Model { get; set; }
    public bool SupportsDuplex { get; set; }  // Default 0
    public bool SupportsLamination { get; set; }  // Default 0
    public bool SupportsEncodingInTandem { get; set; }  // Default 0 — print+encode together
    public PrinterConnectionType? ConnectionType { get; set; }  // USB / Network
    public decimal? RibbonCostEstimate { get; set; }  // For cost tracking (FR-PRN-8)
    public decimal? CardCostEstimate { get; set; }
}
