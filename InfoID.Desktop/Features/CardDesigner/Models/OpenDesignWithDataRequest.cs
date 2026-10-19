using System.Data;
using InfoID.Desktop.Features.Database.Models;

namespace InfoID.Desktop.Features.CardDesigner.Models;

/// <summary>
/// Navigation parameter for the Card Designer: open a saved design with these rows as its
/// data (e.g. the visitors selected on the Visitors page) and, optionally, go straight to
/// the Print dialog.
/// </summary>
/// <param name="TemplateId">The saved design to open.</param>
/// <param name="Data">Rows to show in the design's data grid.</param>
/// <param name="SourceType">Kind of source, for the grid's read-only rules.</param>
/// <param name="SourceName">Shown as the data source name.</param>
/// <param name="KeyColumn">Column that identifies each row (print status / Print history).</param>
/// <param name="OpenPrintDialog">Open the Print dialog once the design is ready.</param>
public sealed record OpenDesignWithDataRequest(
    long TemplateId,
    DataTable Data,
    DatabaseType SourceType,
    string SourceName,
    string? KeyColumn,
    bool OpenPrintDialog);
