using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public sealed partial class SignatureElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Signature;

    [ObservableProperty] private string? _assetReference;
    [ObservableProperty] private bool _transparentBackground = true;
    [ObservableProperty] private string _colorHex = "#000000";
}
