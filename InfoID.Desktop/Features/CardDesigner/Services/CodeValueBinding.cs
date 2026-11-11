using System.Collections.Generic;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// The value a QR code or barcode encodes for one record. The Properties panel offers two
/// ways to bind it: put "{{Field}}" in Value itself, or enter a merge field under
/// Parameters → Data binding (which overrides Value). Either way each record (e.g. each
/// visitor or employee) gets its own code; a value without "{{…}}" is encoded as typed.
/// </summary>
//public static class CodeValueBinding
//{
//    /// <summary>The expression to encode: Data binding when set, otherwise Value. Null for
//    /// elements that aren't codes.</summary>
//    public static string? Expression(DesignerElement element) => element switch
//    {
//        QrCodeElement qr => string.IsNullOrWhiteSpace(qr.DataBindingExpression) ? qr.Value : qr.DataBindingExpression,
//        BarcodeElement barcode => string.IsNullOrWhiteSpace(barcode.DataBindingExpression) ? barcode.Value : barcode.DataBindingExpression,
//        _ => null,
//    };

//    public static bool IsBound(string? expression) =>
//        expression is not null && expression.Contains("{{", System.StringComparison.Ordinal);

//    /// <summary>The value to encode for this record's fields.</summary>
//    public static string? Resolve(DesignerElement element, IReadOnlyDictionary<string, string> fields, IDataBindingEvaluator evaluator)
//    {
//        var expression = Expression(element);
//        if (expression is null) return null;
//        return IsBound(expression) ? evaluator.Evaluate(expression, fields) : expression;
//    }
//}
public static class CodeValueBinding
{
    /// <summary>The expression to encode. Order of precedence:
    ///   1. "Combine fields" / legacy Data binding (DataBindingExpression), when set;
    ///   2. Database source: prefix + {{Column}} + suffix ("" when no field is chosen);
    ///   3. Fixed source: the typed Value (may itself contain {{Field}}).
    /// Null for elements that aren't codes.</summary>
    public static string? Expression(DesignerElement element) => element switch
    {
        QrCodeElement qr => Build(qr.SourceType, qr.DatabaseColumn, qr.Prefix, qr.Suffix, qr.DataBindingExpression, qr.Value),
        BarcodeElement bc => Build(bc.SourceType, bc.DatabaseColumn, bc.Prefix, bc.Suffix, bc.DataBindingExpression, bc.Value),
        _ => null,
    };

    /// <summary>True when the code is set to take its value from a database field.</summary>
    public static bool IsDatabaseSource(DesignerElement element) => element switch
    {
        QrCodeElement qr => qr.SourceType == CodeValueSource.Database,
        BarcodeElement bc => bc.SourceType == CodeValueSource.Database,
        _ => false,
    };

    public static bool IsBound(string? expression) =>
        expression is not null && expression.Contains("{{", System.StringComparison.Ordinal);

    /// <summary>The value to encode for this record's fields. A database field that is
    /// empty for this record gives "" (not a lone prefix such as "EMP-"), so the print
    /// check can report it instead of printing a meaningless code.</summary>
    public static string? Resolve(DesignerElement element, IReadOnlyDictionary<string, string> fields, IDataBindingEvaluator evaluator)
    {
        var expression = Expression(element);
        if (expression is null) return null;

        if (SingleColumn(element) is { } column &&
            string.IsNullOrWhiteSpace(evaluator.Evaluate("{{" + column + "}}", fields)))
            return string.Empty;

        return IsBound(expression) ? evaluator.Evaluate(expression, fields) : expression;
    }

    private static string Build(CodeValueSource source, string? column, string? prefix, string? suffix,
        string? combined, string? value)
    {
        if (!string.IsNullOrWhiteSpace(combined)) return combined;
        if (source == CodeValueSource.Database)
            return string.IsNullOrWhiteSpace(column)
                ? string.Empty
                : (prefix ?? string.Empty) + "{{" + column.Trim() + "}}" + (suffix ?? string.Empty);
        return value ?? string.Empty;
    }

    /// <summary>The one column a Database-source code encodes (no "combine fields"), else null.</summary>
    private static string? SingleColumn(DesignerElement element) => element switch
    {
        QrCodeElement { SourceType: CodeValueSource.Database } qr
            when string.IsNullOrWhiteSpace(qr.DataBindingExpression) && !string.IsNullOrWhiteSpace(qr.DatabaseColumn)
            => qr.DatabaseColumn.Trim(),
        BarcodeElement { SourceType: CodeValueSource.Database } bc
            when string.IsNullOrWhiteSpace(bc.DataBindingExpression) && !string.IsNullOrWhiteSpace(bc.DatabaseColumn)
            => bc.DatabaseColumn.Trim(),
        _ => null,
    };
}
