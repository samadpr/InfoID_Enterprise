using InfoID.Domain.Common;
using InfoID.Domain.Entities.PrintingModule;
using InfoID.Domain.Entities.TemplateModule;

namespace InfoID.Domain.Entities.OrganizationModule;

/// <summary>
/// Multi-branch/multi-location support within a single installation (FR-ENT-2). Each branch can have its own default template, printer and numbering sequence.
/// </summary>
public class Branch : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }  // Short code used in card numbering prefix
    public string? Address { get; set; }
    public string? NumberingPrefix { get; set; }  // Prefix for auto-generated CardNumber
    public long? DefaultPrinterProfileId { get; set; }  // Nullable — set after a printer profile exists
    public PrinterProfile? DefaultPrinterProfile { get; set; }
    public long? DefaultTemplateId { get; set; }  // Nullable — set after a template exists
    public Template? DefaultTemplate { get; set; }
}
