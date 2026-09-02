using System;
using System.Collections.Generic;
using InfoID.Desktop.Features.BlankCard.Models;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>
/// The full serializable state of one card design -- front + back, format, grid
/// settings and metadata. This is the single source of truth a designer tab renders
/// from; the canvas, layers panel and properties panel all read/write into this tree
/// rather than each keeping their own copy of "the design" (Part 3).
///
/// SchemaVersion enables safe .infoid import migration later (Part 66) -- bump it
/// whenever a breaking shape change is made to this file, and add a migrator keyed off
/// the old version rather than guessing at unknown fields.
/// </summary>
public sealed class CardDesignDocument
{
    public const int CurrentSchemaVersion = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled design";
    public string? Description { get; set; }
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int VersionNumber { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string CardFormatId { get; set; } = "cr80";
    public double WidthMm { get; set; } = 85.60;
    public double HeightMm { get; set; } = 53.98;
    public CardOrientation Orientation { get; set; } = CardOrientation.Landscape;
    public double CornerRadiusMm { get; set; } = 3.18;
    public double BleedMm { get; set; } = 3.0;
    public double SafeZoneMm { get; set; } = 3.0;

    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public double GridSizeMm { get; set; } = 5.0;

    /// <summary>The Template.Id this document was last saved as, or null if never
    /// saved. Drives Save-vs-Save-As-style insert/update logic in CardDesignRepository.
    /// Not part of the .infoid export envelope -- it's a local persistence detail, not
    /// portable document content.</summary>
    public long? PersistedTemplateId { get; set; }

    public CardDesignSide Front { get; set; } = new() { Side = CardSide.Front };
    public CardDesignSide Back { get; set; } = new() { Side = CardSide.Back };

    public Dictionary<string, string> Metadata { get; set; } = new();

    public CardDesignSide GetSide(CardSide side) => side == CardSide.Front ? Front : Back;
}