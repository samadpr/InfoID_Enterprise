namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public enum BackgroundKind { Solid, Gradient, Image, Transparent }

public sealed class BackgroundSettings
{
    public BackgroundKind Kind { get; set; } = BackgroundKind.Solid;
    public string ColorHex { get; set; } = "#FFFFFF";
    public string? GradientStartHex { get; set; }
    public string? GradientEndHex { get; set; }
    public double GradientAngle { get; set; }
    public string? AssetReference { get; set; }
}