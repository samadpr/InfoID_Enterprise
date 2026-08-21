using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.TemplateModule;

/// <summary>
/// One canvas object (layer) on a template: text, image, shape, barcode, QR code, or a dynamic/mail-merge field (FR-DES-2 to FR-DES-8).
/// </summary>
public class TemplateElement : BaseEntity
{
    public long TemplateId { get; set; }
    public Template? Template { get; set; }
    public CardSide Side { get; set; }  // Front / Back
    public TemplateElementType ElementType { get; set; }  // Text / Image / Shape / QRCode / Barcode / DynamicField
    public string? BoundFieldName { get; set; }  // Cardholder field or CustomFieldDefinition.FieldKey this element is bound to
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal Rotation { get; set; }  // Default 0
    public int ZIndex { get; set; }  // Layer stack order
    public bool LayerLocked { get; set; }  // Default 0
    public bool LayerVisible { get; set; }  // Default 1
    public string? StyleJson { get; set; }  // Font, color, gradients, etc.
    public string? ConditionalVisibilityRule { get; set; }  // Rule-based visibility (FR-DES-11)
}
