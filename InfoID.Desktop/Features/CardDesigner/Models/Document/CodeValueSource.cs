namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>Where a QR code's or barcode's encoded value comes from. Designs store the
/// value, so add new options at the end only.</summary>
public enum CodeValueSource
{
    /// <summary>The typed Value (may still contain "{{Field}}" for advanced use).</summary>
    Fixed,
    /// <summary>A database field chosen from the list, with optional prefix/suffix.</summary>
    Database,
}
