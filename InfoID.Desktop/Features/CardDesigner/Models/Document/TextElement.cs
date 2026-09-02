using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public sealed partial class TextElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Text;

    [ObservableProperty] private string _text = "Text";
    [ObservableProperty] private string _fontFamily = "Segoe UI";
    [ObservableProperty] private double _fontSize = 12;
    [ObservableProperty] private bool _bold;
    [ObservableProperty] private bool _italic;
    [ObservableProperty] private bool _underline;
    [ObservableProperty] private string _colorHex = "#000000";
    [ObservableProperty] private TextAlignmentX _horizontalAlignment = TextAlignmentX.Left;
    [ObservableProperty] private TextAlignmentY _verticalAlignment = TextAlignmentY.Top;
    [ObservableProperty] private double _letterSpacing;
    [ObservableProperty] private double _lineSpacing = 1.0;
    [ObservableProperty] private bool _autoFit;
    [ObservableProperty] private double _minFontSize = 6;
    [ObservableProperty] private double _maxFontSize = 72;
    [ObservableProperty] private bool _wrapText = true;
}

public enum TextAlignmentX { Left, Center, Right, Justify }
public enum TextAlignmentY { Top, Middle, Bottom }
