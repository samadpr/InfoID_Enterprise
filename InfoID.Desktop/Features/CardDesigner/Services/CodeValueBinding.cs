using System.Collections.Generic;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// The value a QR code or barcode encodes for one record. The Properties panel offers two
/// ways to bind it: put "{{Field}}" in Value itself, or enter a merge field under
/// Parameters → Data binding (which overrides Value). Either way each record (e.g. each
/// visitor or employee) gets its own code; a value without "{{…}}" is encoded as typed.
/// </summary>
public static class CodeValueBinding
{
    /// <summary>The expression to encode: Data binding when set, otherwise Value. Null for
    /// elements that aren't codes.</summary>
    public static string? Expression(DesignerElement element) => element switch
    {
        QrCodeElement qr => string.IsNullOrWhiteSpace(qr.DataBindingExpression) ? qr.Value : qr.DataBindingExpression,
        BarcodeElement barcode => string.IsNullOrWhiteSpace(barcode.DataBindingExpression) ? barcode.Value : barcode.DataBindingExpression,
        _ => null,
    };

    public static bool IsBound(string? expression) =>
        expression is not null && expression.Contains("{{", System.StringComparison.Ordinal);

    /// <summary>The value to encode for this record's fields.</summary>
    public static string? Resolve(DesignerElement element, IReadOnlyDictionary<string, string> fields, IDataBindingEvaluator evaluator)
    {
        var expression = Expression(element);
        if (expression is null) return null;
        return IsBound(expression) ? evaluator.Evaluate(expression, fields) : expression;
    }
}
