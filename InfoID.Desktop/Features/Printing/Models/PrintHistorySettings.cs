using System;

namespace InfoID.Desktop.Features.Printing.Models;

/// <summary>How long a printed card is valid (FR-PRN-6 card records). Stored per design.</summary>
public enum CardValidityMode
{
    /// <summary>Cards are recorded without an expiry date (the default).</summary>
    NoExpiry,
    OneYear,
    TwoYears,
    ThreeYears,
    /// <summary>Every card in the run expires on one fixed date.</summary>
    UntilDate,
    /// <summary>Each record's expiry comes from a data column (e.g. "ValidTill").</summary>
    FromColumn,
}

/// <summary>Per-design choices for recording printed cards in Print history: which data
/// column identifies the person (so a second print of the same person is recognized as a
/// reprint), which column holds the name, and how long the cards are valid. Remembered per
/// design in PrintHistorySettings.json; null columns mean "auto-detect".</summary>
public sealed class PrintHistorySettings
{
    /// <summary>Column holding the person's ID (EmployeeNo, RollNo...). Null = auto-detect;
    /// PrintHistoryPlanner.NoColumn = the user chose not to use one.</summary>
    public string? PersonIdColumn { get; set; }

    /// <summary>Column holding the person's name. Null = auto-detect.</summary>
    public string? NameColumn { get; set; }

    public CardValidityMode Validity { get; set; } = CardValidityMode.NoExpiry;

    /// <summary>Used when <see cref="Validity"/> is UntilDate.</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>Used when <see cref="Validity"/> is FromColumn.</summary>
    public string? ExpiryColumn { get; set; }
}

/// <summary>A "Card valid for" choice as shown in the Print dialog.</summary>
public sealed record CardValidityOption(CardValidityMode Mode, string Label);
