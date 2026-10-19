using System.Text.Json;

namespace InfoID.Application.Reporting;

/// <summary>One changed value of an audit entry: field, before, after.</summary>
public sealed record AuditChange(string Field, string Before, string After);

/// <summary>
/// Turns an audit entry's before/after JSON into readable field-by-field changes, for
/// the Audit log page and the exported audit report (so both show the same thing).
/// </summary>
public static class AuditFormatting
{
    private const int MaxArrayItems = 12;

    /// <summary>Fields with their before and after values. With both sides, only fields
    /// that differ; with one side (created / deleted / an event), all of its fields.</summary>
    public static IReadOnlyList<AuditChange> Changes(string? beforeJson, string? afterJson)
    {
        var before = Flatten(beforeJson);
        var after = Flatten(afterJson);
        var fields = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).ToList();
        var changes = new List<AuditChange>();
        foreach (var field in fields)
        {
            before.TryGetValue(field, out var b);
            after.TryGetValue(field, out var a);
            b ??= "";
            a ??= "";
            if (before.Count > 0 && after.Count > 0 && b == a) continue;
            if (b.Length == 0 && a.Length == 0) continue;
            changes.Add(new AuditChange(Humanize(field), b, a));
        }
        return changes;
    }

    /// <summary>"Name: Old → New; Elements: 4 → 6" for a single report cell.</summary>
    public static string ChangesText(string? beforeJson, string? afterJson, int maxLength = 400)
    {
        var parts = Changes(beforeJson, afterJson).Select(c =>
            c.Before.Length == 0 ? $"{c.Field}: {c.After}"
            : c.After.Length == 0 ? $"{c.Field}: {c.Before} (removed)"
            : $"{c.Field}: {c.Before} → {c.After}");
        var text = string.Join("; ", parts);
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    /// <summary>Top-level fields of a JSON object as display strings. A JSON value that
    /// isn't an object becomes a single "Value" field.</summary>
    private static Dictionary<string, string?> Flatten(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in doc.RootElement.EnumerateObject())
                    result[property.Name] = Display(property.Value);
            }
            else
            {
                result["Value"] = Display(doc.RootElement);
            }
        }
        catch (JsonException)
        {
            result["Value"] = json; // truncated or not JSON: show as text
        }
        return result;
    }

    private static string? Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => ArrayText(value),
        JsonValueKind.Object => string.Join(", ", value.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .Select(p => $"{Humanize(p.Name)}: {Display(p.Value)}")),
        _ => value.GetRawText(),
    };

    private static string ArrayText(JsonElement array)
    {
        var items = array.EnumerateArray().Select(Display).Where(s => !string.IsNullOrEmpty(s)).ToList();
        return items.Count <= MaxArrayItems
            ? string.Join(", ", items)
            : string.Join(", ", items.Take(MaxArrayItems)) + $" … (+{items.Count - MaxArrayItems})";
    }

    /// <summary>"FrontElements" -> "Front elements".</summary>
    public static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
