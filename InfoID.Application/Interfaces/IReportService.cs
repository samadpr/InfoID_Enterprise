using InfoID.Application.Reporting;

namespace InfoID.Application.Interfaces;

/// <summary>Predefined reports (SRS 4.11, FR-REP-1 to FR-REP-3) built from Print history.</summary>
public interface IReportService
{
    Task<ReportTable> GenerateAsync(ReportRequest request, CancellationToken ct = default);

    /// <summary>Design names that appear in Print history, for the design filter.</summary>
    Task<IReadOnlyList<string>> GetDesignNamesAsync(CancellationToken ct = default);
}
