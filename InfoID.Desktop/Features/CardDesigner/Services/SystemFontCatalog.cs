using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Discovers fonts actually installed on this machine via Avalonia's cross-platform
/// <see cref="FontManager"/> API (Part 16: "It should discover available fonts installed
/// on the machine... Use Avalonia's font APIs/platform font discovery"), instead of
/// forcing the user to type a font name into a text box.
///
/// Computed once and cached -- the installed font set doesn't change while the app is
/// running, and enumerating it on every Properties-panel render would be wasteful.
/// </summary>
public static class SystemFontCatalog
{
    private static IReadOnlyList<string>? _names;

    /// <summary>Sorted, de-duplicated list of installed font family names. Falls back to
    /// a small set of near-universal names if font enumeration itself fails on some
    /// platform/headless environment -- the font picker must never crash the Properties
    /// panel just because the OS couldn't be asked what's installed.</summary>
    public static IReadOnlyList<string> FontFamilyNames
    {
        get
        {
            if (_names is not null) return _names;

            try
            {
                _names = FontManager.Current.SystemFonts
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                _names = new[] { "Arial", "Segoe UI", "Calibri", "Times New Roman", "Verdana" };
            }

            // Always guarantee at least the document's own default is selectable, even
            // on a machine where it genuinely isn't installed -- the ComboBox binds
            // TwoWay straight to TextElement.FontFamily/DataFieldElement.FontFamily, so
            // an unlisted saved value would otherwise show as no selection at all.
            if (_names.Count == 0) _names = new[] { "Segoe UI" };

            return _names;
        }
    }

    /// <summary>FontFamilyNames with whatever's in RecentFontsTracker floated to the
    /// front (most-recently-used first), followed by the remaining installed fonts in
    /// their usual alphabetical order -- fonts the tracker remembers that are no
    /// longer actually installed are silently dropped rather than shown as dead
    /// entries. Recomputed on every access (font lists are small, tens to a couple
    /// hundred entries) rather than cached, since -- unlike the installed-font set
    /// itself -- "recent" is expected to actually change while the app runs.</summary>
    public static IReadOnlyList<string> FontFamilyNamesWithRecentFirst
    {
        get
        {
            var all = FontFamilyNames;
            var recent = RecentFontsTracker.Recent
                .Where(f => all.Contains(f, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (recent.Count == 0) return all;

            var rest = all.Where(f => !recent.Contains(f, StringComparer.OrdinalIgnoreCase));
            return recent.Concat(rest).ToList();
        }
    }

    /// <summary>Ensures a specific font name (e.g. a design's already-saved
    /// FontFamily, which may not be installed on this machine) shows up in the picker
    /// even if FontManager didn't report it -- Part 16's "handle unavailable fonts
    /// gracefully" without silently discarding the saved value.</summary>
    public static IReadOnlyList<string> WithGuaranteed(string? fontFamilyName)
    {
        var baseList = FontFamilyNames;
        if (string.IsNullOrWhiteSpace(fontFamilyName) ||
            baseList.Contains(fontFamilyName, StringComparer.OrdinalIgnoreCase))
        {
            return baseList;
        }

        var combined = new List<string>(baseList.Count + 1) { fontFamilyName };
        combined.AddRange(baseList);
        return combined;
    }
}
