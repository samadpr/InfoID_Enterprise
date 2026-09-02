using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public enum QrErrorCorrection { Low, Medium, Quartile, High }

public sealed partial class QrCodeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.QrCode;

    [ObservableProperty] private string _value = "https://";
    [ObservableProperty] private QrErrorCorrection _errorCorrection = QrErrorCorrection.Medium;
    [ObservableProperty] private string _foregroundColorHex = "#000000";
    [ObservableProperty] private string _backgroundColorHex = "#FFFFFF";
    [ObservableProperty] private double _marginMm = 1;
}
