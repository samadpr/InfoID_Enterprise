using InfoID.Application.Dtos.Encoding;

namespace InfoID.Application.Interfaces;

/// <summary>Tag UID ↔ record mapping on the Issuance tables (Card.RFIDUid, RfidEncodeLog).</summary>
public interface INfcEncodingService
{
    /// <summary>The Draft/Issued/Active card that already has this UID, or null.</summary>
    Task<NfcTagOwnerDto?> FindTagOwnerAsync(string uid, CancellationToken ct = default);

    /// <summary>Links a written + verified tag to the person's card for this design (a Draft card
    /// is created when they have none). Throws InvalidOperationException if the UID belongs to someone else.</summary>
    Task<NfcTagAssignmentResult> AssignTagAsync(NfcTagAssignment assignment, CancellationToken ct = default);

    /// <summary>Logs a failed write against the person's card when they have one.</summary>
    Task RecordFailureAsync(long? templateId, string? personKey, string reason, CancellationToken ct = default);

    Task RecordBatchAsync(NfcEncodingBatchSummary summary, CancellationToken ct = default);
}
