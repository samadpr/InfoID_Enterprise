namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>
/// Every element kind the schema knows about. Deliberately includes types not yet
/// insertable from the toolbar (Barcode, QrCode, Photo, Signature, DataField, Svg,
/// Line, Group, Watermark) so the document model, JSON schema and layers panel are
/// future-proof -- adding the tool button for one of these later does not require a
/// document-model or file-format change. See CardDesignerViewModel for which of these
/// currently have a working insert tool.
/// </summary>
public enum ElementType
{
    Text,
    Shape,
    Line,
    Image,
    Photo,
    Signature,
    Svg,
    Barcode,
    QrCode,
    DataField,
    Date,
    Time,
    Counter,
    Watermark,
    Group,
    DateTime
}