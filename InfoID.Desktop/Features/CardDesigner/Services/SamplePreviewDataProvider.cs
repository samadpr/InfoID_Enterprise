using System.Collections.Generic;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>One row of sample data for previewing dynamic fields/barcode/QR/conditional
/// visibility while designing (Part 43). These are NOT real cardholder records -- the
/// Cardholder Management module (Domain Model Module E) doesn't exist yet in this
/// codebase, so there is nothing real to preview against. Once that module lands, Data
/// Preview should switch to pulling actual imported cardholders instead of this fixed
/// list; that swap only needs a different IPreviewDataProvider implementation, not
/// changes to the evaluator or the canvas rendering that consumes it.</summary>
public sealed record PreviewRecord(string Label, IReadOnlyDictionary<string, string> Fields);

public interface IPreviewDataProvider
{
    IReadOnlyList<PreviewRecord> GetSampleRecords();
}

public sealed class SamplePreviewDataProvider : IPreviewDataProvider
{
    public IReadOnlyList<PreviewRecord> GetSampleRecords() => _records;

    private static readonly IReadOnlyList<PreviewRecord> _records = new List<PreviewRecord>
    {
        new("Sample: Abdul Samad", new Dictionary<string, string>
        {
            ["FirstName"] = "Abdul", ["LastName"] = "Samad", ["EmployeeId"] = "EMP-1001",
            ["Department"] = "Engineering", ["JobTitle"] = "Software Engineer",
            ["CardNumber"] = "IN-0001", ["ExpiryDate"] = "2027-12-31",
        }),
        new("Sample: Fatima Noor", new Dictionary<string, string>
        {
            ["FirstName"] = "Fatima", ["LastName"] = "Noor", ["EmployeeId"] = "EMP-1002",
            ["Department"] = "Security", ["JobTitle"] = "Security Officer",
            ["CardNumber"] = "IN-0002", ["ExpiryDate"] = "2027-06-30",
        }),
        new("Sample: Rahul Menon", new Dictionary<string, string>
        {
            ["FirstName"] = "Rahul", ["LastName"] = "Menon", ["EmployeeId"] = "EMP-1003",
            ["Department"] = "HR", ["JobTitle"] = "HR Manager",
            ["CardNumber"] = "IN-0003", ["ExpiryDate"] = "2026-11-15",
        }),
    };
}
