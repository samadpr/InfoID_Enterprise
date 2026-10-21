using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InfoID.LicenseManager.Web.Ui;

/// <summary>Small view helpers that keep the markup of icons, badges and bars identical on every page.</summary>
public static class UiHtml
{
    /// <summary>An icon from the sprite in <c>_IconSprite.cshtml</c> (decorative: hidden from screen readers).</summary>
    public static IHtmlContent Icon(this IHtmlHelper html, string name, string? cssClass = null) =>
        new HtmlString($"<svg class=\"icon{(string.IsNullOrEmpty(cssClass) ? "" : " " + WebUtility.HtmlEncode(cssClass))}\" aria-hidden=\"true\"><use href=\"#i-{WebUtility.HtmlEncode(name)}\"/></svg>");

    /// <summary>A coloured status pill. The CSS class is the status in lower case; the text is made readable ("InProgress" becomes "In progress").</summary>
    public static IHtmlContent StatusBadge(this IHtmlHelper html, string? status, string? textOverride = null)
    {
        var value = status ?? "";
        var css = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var text = textOverride ?? (value == "InProgress" ? "In progress" : value);
        return new HtmlString($"<span class=\"badge {css}\">{WebUtility.HtmlEncode(text)}</span>");
    }

    /// <summary>A horizontal bar for <paramref name="value"/> out of <paramref name="max"/>, drawn as inline SVG (no inline style, so the content-security policy stays strict).</summary>
    public static IHtmlContent HBar(this IHtmlHelper html, int value, int max, int series = 1)
    {
        var percent = max <= 0 ? 0 : Math.Clamp(value * 100.0 / max, 0, 100);
        var width = percent.ToString("0.##", CultureInfo.InvariantCulture);
        return new HtmlString(
            "<svg role=\"img\" aria-label=\"" + width + " percent\" preserveAspectRatio=\"none\">" +
            "<rect class=\"track-bg\" width=\"100%\" height=\"100%\"/>" +
            $"<rect class=\"bar-fill f{series}\" width=\"{width}%\" height=\"100%\"/></svg>");
    }

    /// <summary>Up to two initials of a display name, for the avatar.</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>An audit action name as a sentence: "LicenseCode.Revoked" becomes "License code revoked".</summary>
    public static string ActionLabel(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "";
        var words = new System.Text.StringBuilder();
        foreach (var c in action.Replace('.', ' ').Replace('_', ' '))
        {
            if (char.IsUpper(c) && words.Length > 0 && words[^1] != ' ') words.Append(' ');
            words.Append(c);
        }

        var text = words.ToString().Trim();
        return char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();
    }

    /// <summary>The role as people read it ("SupportAgent" becomes "Support Agent").</summary>
    public static string RoleLabel(string? role) => role switch
    {
        "SuperAdmin" => "Super Admin",
        "LicenseManager" => "License Manager",
        "SupportAgent" => "Support Agent",
        null or "" => "",
        _ => role,
    };

    /// <summary>"yyyy-MM-dd" for dates; a dash when there is none.</summary>
    public static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";

    public static string Date(DateTime? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";

    public static string DateTimeUtc(DateTime? date) => date?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + (date is null ? "-" : " UTC");
}
