using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public enum QrErrorCorrection { Low, Medium, Quartile, High }

/// <summary>GS1's QR Code variant (a QR Code whose payload uses GS1 Application
/// Identifiers, e.g. for supply-chain/product data) vs a plain QR Code. Maps directly
/// to ZXing.Net's QrCodeEncodingOptions.GS1Format. The ID-ALL reference also lists an
/// "Industry" format; there's no equivalent concept in ZXing.Net's QR encoder (it isn't
/// a real, separate encoding mode there), so it isn't offered here -- see
/// BarcodeRenderer's own doc comment.</summary>
public enum QrFormat { Standard, Gs1 }

public sealed partial class QrCodeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.QrCode;

    [ObservableProperty] private string _value = "https://";
    [ObservableProperty] private QrErrorCorrection _errorCorrection = QrErrorCorrection.Medium;
    [ObservableProperty] private string _foregroundColorHex = "#000000";
    [ObservableProperty] private string _backgroundColorHex = "#FFFFFF";
    [ObservableProperty] private double _marginMm = 1;

    /// <summary>Null = let the encoder choose the smallest version (module grid size,
    /// 1-40) that fits Value at the current error-correction level -- the normal,
    /// recommended choice ("Auto" in the Properties panel). A specific 1-40 forces that
    /// exact version; if Value doesn't fit, encoding fails and the element falls back to
    /// a placeholder (see BarcodeRenderer) rather than silently ignoring the request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersionAuto))]
    [NotifyPropertyChangedFor(nameof(VersionValue))]
    private int? _version;

    /// <summary>Null = let the encoder pick whichever of the 8 standard QR mask
    /// patterns (0-7) scores best for this exact payload ("Standard"/Auto in the
    /// Properties panel, and the normal, recommended choice). A specific 0-7 forces
    /// that exact mask -- mainly useful for testing/matching a spec sample, since the
    /// auto-selected mask is virtually always at least as good for real-world
    /// scanning.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMaskAuto))]
    [NotifyPropertyChangedFor(nameof(MaskValue))]
    private int? _maskPattern;

    /// <summary>Auto/specific toggle + a plain non-nullable int the Properties panel's
    /// NumericUpDown can bind to directly (NumericUpDown needs a concrete numeric type,
    /// not int?) -- these wrap Version/MaskPattern rather than duplicating state, so
    /// there is exactly one source of truth for what actually gets encoded.</summary>
    public bool IsVersionAuto
    {
        get => Version is null;
        set => Version = value ? null : VersionValue;
    }

    public int VersionValue
    {
        get => Version ?? 10;
        set => Version = value;
    }

    public bool IsMaskAuto
    {
        get => MaskPattern is null;
        set => MaskPattern = value ? null : MaskValue;
    }

    public int MaskValue
    {
        get => MaskPattern ?? 0;
        set => MaskPattern = value;
    }

    /// <summary>Character encoding for Value before it's fed to the QR encoder (ZXing's
    /// CharacterSet option, e.g. "UTF-8"/"ISO-8859-1"/"Shift_JIS"). Empty/null lets the
    /// encoder use its own default (ISO-8859-1, falling back to byte mode for anything
    /// that doesn't fit it) -- matches "Default" in the Properties panel.</summary>
    [ObservableProperty] private string? _characterSet;

    [ObservableProperty] private QrFormat _format = QrFormat.Standard;
}
