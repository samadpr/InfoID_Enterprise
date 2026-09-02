using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Evaluates the two small expression languages used by the Card Designer's data-driven
/// elements (Part 24/25/43):
///   - Field placeholders: "{{FirstName}} {{LastName}}" -> "Abdul Samad"
///   - Visibility conditions: "Department == 'Security'" -> true/false
///
/// Both evaluators are deliberately simple pattern-matchers, NOT a general expression
/// engine -- there is no eval(), no reflection, no way for a condition string saved in
/// a design to run arbitrary code. That's a hard requirement (Part 44: "Do NOT execute
/// arbitrary C# or arbitrary code"), not just a nice-to-have.
/// </summary>
public interface IDataBindingEvaluator
{
    /// <summary>Replaces every "{{FieldName}}" token in <paramref name="expression"/>
    /// with the matching value from <paramref name="record"/>. A field with no match in
    /// the record is left as-is (e.g. "{{Unknown}}") rather than silently blanked, so a
    /// typo'd or not-yet-imported field is visibly obvious while designing.</summary>
    string Evaluate(string? expression, IReadOnlyDictionary<string, string> record);

    /// <summary>Evaluates a visibility condition. A null/empty/whitespace condition
    /// always means "visible" (no condition set). Supports one or more
    /// "Field == 'Value'" / "Field != 'Value'" comparisons joined with &amp;&amp; or ||
    /// (not mixed in the same expression -- if you need both, this is intentionally not
    /// a full boolean-expression parser). Unparseable conditions default to visible
    /// rather than hidden, so a malformed condition doesn't silently disappear a design
    /// element the user is trying to work on.</summary>
    bool EvaluateCondition(string? condition, IReadOnlyDictionary<string, string> record);
}

public sealed class DataBindingEvaluator : IDataBindingEvaluator
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex ComparisonPattern = new(
        @"^\s*([A-Za-z0-9_]+)\s*(==|!=)\s*(?:'([^']*)'|""([^""]*)""|(\S+))\s*$",
        RegexOptions.Compiled);

    public string Evaluate(string? expression, IReadOnlyDictionary<string, string> record)
    {
        if (string.IsNullOrEmpty(expression)) return string.Empty;

        return TokenPattern.Replace(expression, match =>
        {
            var fieldName = match.Groups[1].Value;
            return record.TryGetValue(fieldName, out var value) ? value : match.Value;
        });
    }

    public bool EvaluateCondition(string? condition, IReadOnlyDictionary<string, string> record)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        // Only one join operator is honored per expression -- deliberately not a full
        // precedence-aware boolean parser (see interface doc).
        if (condition.Contains("&&"))
        {
            var parts = condition.Split("&&", StringSplitOptions.None);
            foreach (var part in parts)
            {
                if (!EvaluateSingle(part, record)) return false;
            }
            return true;
        }

        if (condition.Contains("||"))
        {
            var parts = condition.Split("||", StringSplitOptions.None);
            foreach (var part in parts)
            {
                if (EvaluateSingle(part, record)) return true;
            }
            return false;
        }

        return EvaluateSingle(condition, record);
    }

    private static bool EvaluateSingle(string clause, IReadOnlyDictionary<string, string> record)
    {
        var match = ComparisonPattern.Match(clause);
        if (!match.Success) return true; // unparseable -> default visible, see interface doc

        var field = match.Groups[1].Value;
        var op = match.Groups[2].Value;
        var expected = match.Groups[3].Success ? match.Groups[3].Value
                      : match.Groups[4].Success ? match.Groups[4].Value
                      : match.Groups[5].Value;

        var actual = record.TryGetValue(field, out var v) ? v : string.Empty;
        var equal = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return op == "==" ? equal : !equal;
    }
}
