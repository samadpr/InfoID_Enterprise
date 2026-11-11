using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.Encoding;

/// <summary>The card a tag UID currently belongs to.</summary>
public sealed class NfcTagOwnerDto
{
    public long CardId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public long? TemplateId { get; set; }
    public string? PersonKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public CardStatus Status { get; set; }
}

/// <summary>A tag that was written and verified, to link to its record.</summary>
public sealed class NfcTagAssignment
{
    public long? TemplateId { get; set; }
    public string PersonKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
}

public sealed class NfcTagAssignmentResult
{
    public long CardId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    /// <summary>True when the person had no card yet and a Draft card was created.</summary>
    public bool CreatedCard { get; set; }
    /// <summary>The UID this card had before (its old tag is no longer linked).</summary>
    public string? PreviousUid { get; set; }
}

/// <summary>One "Write NFC tags" run, for the audit trail (IDs and counts only).</summary>
public sealed class NfcEncodingBatchSummary
{
    public string DesignName { get; set; } = string.Empty;
    public long? TemplateId { get; set; }
    public int Records { get; set; }
    public int Written { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int UidsSavedToData { get; set; }
    public bool Stopped { get; set; }
}
