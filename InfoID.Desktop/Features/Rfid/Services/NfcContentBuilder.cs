using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using InfoID.CardEncoding.Nfc;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.Rfid.Services;

public enum NfcIssueSeverity { Info, Warning, Error }

public sealed record NfcContentIssue(NfcIssueSeverity Severity, string Message);

/// <summary>The NFC content for one record: the NDEF records to write, the resolved link
/// and text (for display), the exact size on a Type 2 tag, and any problems.</summary>
public sealed record NfcContent(
    IReadOnlyList<NdefRecord> Records,
    string? Url,
    string? Text,
    int SizeBytes,
    IReadOnlyList<NfcContentIssue> Issues)
{
    /// <summary>Errors block writing (Step 4); warnings and info don't.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == NfcIssueSeverity.Error);

    public bool FitsOn(NfcTagModel model) => SizeBytes > 0 && SizeBytes <= NfcTagModels.CapacityBytes(model);
}

/// <summary>
/// The one place that turns a design's NFC settings plus one record's values into NFC
/// content. The designer preview and the card writer both use it, so what is previewed is
/// exactly what is written. {{Field}} tokens are resolved by the designer's existing
/// IDataBindingEvaluator; the size is the complete encoded NDEF message (record headers,
/// TLV and terminator, rounded to whole tag pages) from InfoID.CardEncoding.
/// </summary>
public sealed class NfcContentBuilder
{
    // Same token rule as DataBindingEvaluator, so "is this a field?" matches what resolves.
    private static readonly Regex TokenPattern = new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex AnyBraces = new(@"\{\{[^{}]*\}\}", RegexOptions.Compiled);
    private static readonly Regex UsableFieldName = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private readonly IDataBindingEvaluator _evaluator;

    public NfcContentBuilder(IDataBindingEvaluator evaluator) => _evaluator = evaluator;

    /// <summary>Whether a data column can be used as {{Name}} (letters, digits and _ only).</summary>
    public static bool IsUsableFieldName(string column) => UsableFieldName.IsMatch(column);

    public NfcContent Build(NfcEncodingSettings settings, IReadOnlyDictionary<string, string> fields)
    {
        var issues = new List<NfcContentIssue>();
        var records = new List<NdefRecord>();
        string? url = null;
        string? text = null;

        if (settings.Mode is NfcContentMode.Url or NfcContentMode.UrlAndText)
        {
            var template = (settings.UrlTemplate ?? string.Empty).Trim();
            if (template.Length == 0)
            {
                issues.Add(Error("Enter the link to write."));
            }
            else
            {
                CheckFields(template, fields, "link", issues);

                // Field values inside a link are URL-encoded so values with spaces or symbols
                // still make a valid link -- except when the whole link IS one field (the
                // column already holds a complete URL).
                var wholeLinkIsOneField = TokenPattern.Match(template) is { Success: true } m && m.Value.Length == template.Length;
                url = wholeLinkIsOneField ? _evaluator.Evaluate(template, fields) : _evaluator.Evaluate(template, Escaped(fields));

                if (!AnyBraces.IsMatch(url) && !IsValidLink(url))
                    issues.Add(Error($"\"{url}\" isn't a valid link. Start it with https://, http://, tel: or mailto:."));

                records.Add(NdefRecord.ForUri(url));
            }
        }

        if (settings.Mode is NfcContentMode.Text or NfcContentMode.UrlAndText)
        {
            var template = settings.TextTemplate ?? string.Empty;
            if (template.Trim().Length == 0)
            {
                issues.Add(Error("Enter the text to write."));
            }
            else
            {
                CheckFields(template, fields, "text", issues);
                text = _evaluator.Evaluate(template, fields);
                var language = string.IsNullOrWhiteSpace(settings.TextLanguage) ? "en" : settings.TextLanguage.Trim();
                records.Add(NdefRecord.ForText(text, language));
            }
        }

        var size = records.Count == 0 ? 0 : NdefMessage.SizeOnType2Tag(records);
        var tag = settings.SizeCheckTag;
        var capacity = NfcTagModels.CapacityBytes(tag);
        if (size > capacity)
        {
            var fitting = NfcTagModels.All.Where(t => size <= NfcTagModels.CapacityBytes(t))
                                          .Select(NfcTagModels.DisplayName).ToList();
            issues.Add(Error(fitting.Count == 0
                ? $"The content needs {size} bytes, more than any supported tag holds (NTAG216: 872). Shorten the link or text."
                : $"The content needs {size} bytes but {NfcTagModels.DisplayName(tag)} holds {capacity}. It fits on: {string.Join(", ", fitting)}."));
        }

        return new NfcContent(records, url, text, size, issues.Distinct().ToList());
    }

    private static void CheckFields(string template, IReadOnlyDictionary<string, string> fields, string where, List<NfcContentIssue> issues)
    {
        foreach (Match braces in AnyBraces.Matches(template))
        {
            var token = TokenPattern.Match(braces.Value);
            if (!token.Success || token.Value.Length != braces.Value.Length)
            {
                issues.Add(Error($"{braces.Value} in the {where} isn't a valid field. Field names can only contain letters, digits and _."));
                continue;
            }

            var name = token.Groups[1].Value;
            if (!fields.TryGetValue(name, out var value))
                issues.Add(Error($"Field \"{name}\" in the {where} isn't in the connected data."));
            else if (string.IsNullOrWhiteSpace(value))
                issues.Add(Error($"Field \"{name}\" is empty for this record."));
        }
    }

    private static Dictionary<string, string> Escaped(IReadOnlyDictionary<string, string> fields)
    {
        var escaped = new Dictionary<string, string>(fields.Count);
        foreach (var (key, value) in fields)
            escaped[key] = Uri.EscapeDataString(value ?? string.Empty);
        return escaped;
    }

    private static bool IsValidLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "tel" or "mailto";

    private static NfcContentIssue Error(string message) => new(NfcIssueSeverity.Error, message);
}
