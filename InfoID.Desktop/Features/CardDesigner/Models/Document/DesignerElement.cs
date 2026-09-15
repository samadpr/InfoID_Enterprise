using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>
/// Base type for every object placed on a card side. Positions/sizes are stored in
/// millimeters (matching CardDesignDocument's physical units), not pixels -- the canvas
/// converts to device pixels only at render time, which is what keeps designs print-
/// accurate regardless of zoom/monitor DPI (Part 65).
///
/// Inherits ObservableObject (CommunityToolkit.Mvvm) so every property setter raises
/// PropertyChanged. This is the fix for the "property panel edits a value but the
/// canvas doesn't redraw" bug (Part 78): DesignerElement used to be a plain POCO, so
/// changing Text/FontSize/ColorHex/etc. from the Properties panel silently updated the
/// model with no notification for anything to react to. CardDesignTabViewModel now
/// subscribes to PropertyChanged on every element in the document (see HookElement) and
/// forwards it into the existing NotifyDocumentChanged + MarkDirty pipeline, which the
/// canvas and autosave already listen to. That gives the two-way flow requested:
///   Property panel edit -> element.PropertyChanged -> DocumentChanged -> canvas redraw
///   Canvas drag/inline edit -> element.PropertyChanged -> Properties panel refresh
/// in one mechanism, instead of scattered manual InvalidateVisual() calls.
///
/// Polymorphic JSON: System.Text.Json serializes/deserializes the concrete subtype
/// automatically via the "$type" discriminator below, so CardDesignDocument can hold a
/// heterogeneous List&lt;DesignerElement&gt; and round-trip it through .infoid export
/// without a manual type-switch. ObservableObject does not add any extra serialized
/// members, so this does not change the JSON shape.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(PhotoElement), "photo")]
[JsonDerivedType(typeof(SignatureElement), "signature")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
[JsonDerivedType(typeof(QrCodeElement), "qrcode")]
[JsonDerivedType(typeof(DataFieldElement), "datafield")]
[JsonDerivedType(typeof(PenElement), "pen")]
public abstract partial class DesignerElement : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "Element";

    public abstract ElementType ElementType { get; }

    // Physical position/size in millimeters, relative to the card's top-left corner.
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 20;
    [ObservableProperty] private double _height = 10;
    [ObservableProperty] private double _rotation;

    [ObservableProperty] private double _opacity = 1.0;
    [ObservableProperty] private bool _visible = true;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private int _zIndex;

    /// <summary>Optional dynamic-field binding, e.g. "{{FirstName}} {{LastName}}" for a
    /// text element, or "{{EmployeeId}}" for a barcode/QR value (Part 24).</summary>
    [ObservableProperty] private string? _dataBindingExpression;

    /// <summary>Rule-based visibility, e.g. "Department == 'Security'" (Part 25).
    /// Evaluated by a future IConditionalVisibilityEvaluator -- stored here now so
    /// designs authored today don't need a schema migration when that ships.</summary>
    [ObservableProperty] private string? _visibilityCondition;

    /// <summary>Shared identifier for grouped elements (Part 28). Null = not grouped.
    /// Selecting any member on the canvas expands selection to the whole group.</summary>
    [ObservableProperty] private string? _groupId;
}
