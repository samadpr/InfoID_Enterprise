using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>The QR Code Properties panel's "Code Page" ComboBox -- backs
/// QrCodeElement.CharacterSet (a plain string ZXing.Net's QrCodeEncodingOptions.
/// CharacterSet accepts directly, e.g. "UTF-8"/"ISO-8859-1"/"Shift_JIS"). "Default"
/// is a display-only stand-in for null/empty (the encoder's own default), translated
/// by DisplayConverter below -- everything else passes straight through unchanged.</summary>
public static class QrCharacterSetOptions
{
    private const string DefaultLabel = "Default";

    public static readonly IReadOnlyList<string> All = new[]
    {
        DefaultLabel, "UTF-8", "ISO-8859-1", "Windows-1252", "Shift_JIS", "ASCII",
    };

    public static readonly IValueConverter DisplayConverter = new CharacterSetDisplayConverter();

    private sealed class CharacterSetDisplayConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            string.IsNullOrWhiteSpace(value as string) ? DefaultLabel : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value as string == DefaultLabel ? null : value;
    }
}
