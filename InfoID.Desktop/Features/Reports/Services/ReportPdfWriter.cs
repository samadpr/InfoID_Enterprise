using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InfoID.Application.Reporting;
using SkiaSharp;

namespace InfoID.Desktop.Features.Reports.Services;

/// <summary>
/// Writes a <see cref="ReportTable"/> as a vector PDF (selectable, searchable text) with
/// SkiaSharp's PDF backend, which Avalonia already ships -- no extra package.
///
/// Layout: A4 landscape. First page: organization, title, filters, generation time and
/// the summary figures; then the table. The header row repeats on every page, rows are
/// zebra-striped, numbers right-aligned, long text shortened with "...", and every page
/// has a "Page n of N" footer. Characters the main font lacks (e.g. Indian scripts)
/// fall back to a system font that has them, and all text is shaped with HarfBuzz
/// (also shipped with Avalonia) so joined scripts render correctly.
/// </summary>
public static class ReportPdfWriter
{
    private const float PageWidth = 842f;   // A4 landscape, points
    private const float PageHeight = 595f;
    private const float Margin = 36f;
    private const float RowHeight = 18f;
    private const float HeaderRowHeight = 22f;
    private const float CellPadding = 5f;
    private const float FooterHeight = 24f;

    private static readonly SKColor Brand = new(0xDA, 0x30, 0x25);
    private static readonly SKColor TextDark = new(0x1F, 0x23, 0x28);
    private static readonly SKColor TextMuted = new(0x6B, 0x72, 0x80);
    private static readonly SKColor Zebra = new(0xF5, 0xF6, 0xF8);
    private static readonly SKColor Rule = new(0xE2, 0xE5, 0xE9);

    public static void Write(ReportTable table, Stream output)
    {
        using var regular = LoadTypeface(SKFontStyle.Normal);
        using var bold = LoadTypeface(SKFontStyle.Bold);
        var fonts = new Fonts(regular, bold);

        var metadata = new SKDocumentPdfMetadata
        {
            Title = table.Title,
            Creator = "InfoID",
            Producer = "InfoID",
            Creation = DateTime.Now,
            Modified = DateTime.Now,
        };

        using var document = SKDocument.CreatePdf(output, metadata);

        var widths = ColumnWidths(table);
        var firstPageTop = MeasureIntro(table);
        var pages = Paginate(table.Rows.Count, firstPageTop);

        for (var p = 0; p < pages.Count; p++)
        {
            using var canvas = document.BeginPage(PageWidth, PageHeight);
            var y = Margin;
            if (p == 0) y = DrawIntro(canvas, table, fonts);

            if (table.Columns.Count > 0)
            {
                y = DrawHeaderRow(canvas, table, widths, y, fonts);
                var (start, count) = pages[p];
                for (var i = 0; i < count; i++)
                    y = DrawRow(canvas, table, widths, table.Rows[start + i], start + i, y, fonts);

                if (table.Rows.Count == 0)
                    DrawText(canvas, "No records match these filters.", Margin, y + 16, fonts.Regular(10), TextMuted);
            }

            DrawFooter(canvas, table, p + 1, pages.Count, fonts);
            document.EndPage();
        }

        document.Close();
    }

    public static byte[] ToBytes(ReportTable table)
    {
        using var ms = new MemoryStream();
        Write(table, ms);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ layout ----

    private static float TableWidth => PageWidth - 2 * Margin;

    private static float[] ColumnWidths(ReportTable table)
    {
        if (table.Columns.Count == 0) return Array.Empty<float>();
        var total = table.Columns.Sum(c => Math.Max(0.1, c.Width));
        return table.Columns.Select(c => (float)(Math.Max(0.1, c.Width) / total * TableWidth)).ToArray();
    }

    /// <summary>Height used by the title block and summary on page 1.</summary>
    private static float MeasureIntro(ReportTable table)
    {
        var y = Margin;
        if (!string.IsNullOrWhiteSpace(table.OrganizationName)) y += 16;
        y += 26; // title
        y += 16; // subtitle
        y += 14; // generated
        if (table.Summary.Count > 0) y += 12 + SummaryRows(table) * 34;
        return y + 12;
    }

    private static int SummaryPerRow => 5;
    private static int SummaryRows(ReportTable table) => (table.Summary.Count + SummaryPerRow - 1) / SummaryPerRow;

    /// <summary>(first row index, row count) per page.</summary>
    private static List<(int Start, int Count)> Paginate(int rowCount, float firstPageTop)
    {
        var bottom = PageHeight - Margin - FooterHeight;
        var firstCapacity = Math.Max(1, (int)((bottom - firstPageTop - HeaderRowHeight) / RowHeight));
        var otherCapacity = Math.Max(1, (int)((bottom - Margin - HeaderRowHeight) / RowHeight));

        var pages = new List<(int, int)>();
        var index = 0;
        var capacity = firstCapacity;
        do
        {
            var count = Math.Min(capacity, rowCount - index);
            pages.Add((index, Math.Max(0, count)));
            index += Math.Max(0, count);
            capacity = otherCapacity;
        } while (index < rowCount);
        return pages;
    }

    // ------------------------------------------------------------------ drawing ----

    private static float DrawIntro(SKCanvas canvas, ReportTable table, Fonts fonts)
    {
        var y = Margin;
        using var accent = new SKPaint { Color = Brand, IsAntialias = true };
        canvas.DrawRect(Margin, y - 4, 40, 3, accent);

        if (!string.IsNullOrWhiteSpace(table.OrganizationName))
        {
            y += 16;
            DrawText(canvas, table.OrganizationName!, Margin, y, fonts.Bold(11), TextMuted);
        }
        y += 26;
        DrawText(canvas, table.Title, Margin, y, fonts.Bold(20), TextDark);
        y += 16;
        DrawText(canvas, table.Subtitle, Margin, y, fonts.Regular(10), TextMuted);
        y += 14;
        DrawText(canvas, $"Generated {table.GeneratedAt:dd MMM yyyy HH:mm} by InfoID", Margin, y, fonts.Regular(9), TextMuted);

        if (table.Summary.Count > 0)
        {
            y += 12;
            var boxWidth = (TableWidth - (SummaryPerRow - 1) * 8) / SummaryPerRow;
            using var boxPaint = new SKPaint { Color = Zebra, IsAntialias = true };
            for (var i = 0; i < table.Summary.Count; i++)
            {
                var col = i % SummaryPerRow;
                var row = i / SummaryPerRow;
                var x = Margin + col * (boxWidth + 8);
                var top = y + row * 34;
                canvas.DrawRoundRect(new SKRect(x, top, x + boxWidth, top + 28), 4, 4, boxPaint);
                DrawText(canvas, Fit(table.Summary[i].Key, boxWidth - 70, fonts.Regular(8.5f)), x + 8, top + 17, fonts.Regular(8.5f), TextMuted);
                DrawTextRight(canvas, table.Summary[i].Value, x + boxWidth - 8, top + 18, fonts.Bold(12), TextDark);
            }
            y += SummaryRows(table) * 34;
        }
        return y + 12;
    }

    private static float DrawHeaderRow(SKCanvas canvas, ReportTable table, float[] widths, float y, Fonts fonts)
    {
        using var fill = new SKPaint { Color = Brand, IsAntialias = true };
        canvas.DrawRect(Margin, y, TableWidth, HeaderRowHeight, fill);
        var x = Margin;
        var font = fonts.Bold(9);
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var column = table.Columns[c];
            var text = Fit(column.Header, widths[c] - 2 * CellPadding, font);
            if (column.Type == ReportColumnType.Number)
                DrawTextRight(canvas, text, x + widths[c] - CellPadding, y + 15, font, SKColors.White);
            else
                DrawText(canvas, text, x + CellPadding, y + 15, font, SKColors.White);
            x += widths[c];
        }
        return y + HeaderRowHeight;
    }

    private static float DrawRow(SKCanvas canvas, ReportTable table, float[] widths, object?[] values, int index, float y, Fonts fonts)
    {
        if (index % 2 == 1)
        {
            using var zebra = new SKPaint { Color = Zebra };
            canvas.DrawRect(Margin, y, TableWidth, RowHeight, zebra);
        }
        using (var rule = new SKPaint { Color = Rule, StrokeWidth = 0.5f })
            canvas.DrawLine(Margin, y + RowHeight, Margin + TableWidth, y + RowHeight, rule);

        var x = Margin;
        var font = fonts.Regular(8.5f);
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var column = table.Columns[c];
            var value = c < values.Length ? values[c] : null;
            var text = Fit(ReportTable.FormatCell(value, column.Type), widths[c] - 2 * CellPadding, font);
            var color = value is int n && n < 0 && column.Type == ReportColumnType.Number ? Brand : TextDark;
            if (column.Type == ReportColumnType.Number)
                DrawTextRight(canvas, text, x + widths[c] - CellPadding, y + 12.5f, font, color);
            else
                DrawText(canvas, text, x + CellPadding, y + 12.5f, font, color);
            x += widths[c];
        }
        return y + RowHeight;
    }

    private static void DrawFooter(SKCanvas canvas, ReportTable table, int page, int pageCount, Fonts fonts)
    {
        var y = PageHeight - Margin + 6;
        using var rule = new SKPaint { Color = Rule, StrokeWidth = 0.5f };
        canvas.DrawLine(Margin, y - 14, Margin + TableWidth, y - 14, rule);
        DrawText(canvas, $"InfoID · {table.Title}", Margin, y, fonts.Regular(8), TextMuted);
        DrawTextRight(canvas, $"Page {page} of {pageCount}", Margin + TableWidth, y, fonts.Regular(8), TextMuted);
    }

    // ------------------------------------------------------------------ text ----

    /// <summary>Draws text with HarfBuzz shaping, so joined scripts (Malayalam, Hindi,
    /// Arabic...) render correctly, not as separate unjoined letters.</summary>
    private static void DrawText(SKCanvas canvas, string text, float x, float y, SKFont font, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var (run, runFont) in Runs(text, font))
        {
            var shaped = Shape(run, runFont);
            if (shaped.Glyphs.Length > 0)
            {
                using var builder = new SKTextBlobBuilder();
                var buffer = builder.AllocatePositionedRun(runFont, shaped.Glyphs.Length);
                buffer.SetGlyphs(shaped.Glyphs);
                buffer.SetPositions(shaped.Positions);
                using var blob = builder.Build();
                if (blob is not null) canvas.DrawText(blob, x, y, paint);
            }
            x += shaped.Width;
        }
    }

    private readonly record struct ShapedRun(ushort[] Glyphs, SKPoint[] Positions, float Width);

    private const int ShapeScale = 512;
    private static readonly Dictionary<IntPtr, HarfBuzzSharp.Font> Shapers = new();

    /// <summary>Shapes one run with HarfBuzz (glyphs + positions relative to the start).
    /// Falls back to plain glyph mapping if the font data can't be read.</summary>
    private static ShapedRun Shape(string text, SKFont font)
    {
        var hbFont = ShaperFor(font.Typeface ?? SKTypeface.Default);
        if (hbFont is null)
        {
            var glyphs = font.GetGlyphs(text);
            var widths = font.GetGlyphWidths(glyphs);
            var positions = new SKPoint[glyphs.Length];
            var advance = 0f;
            for (var i = 0; i < glyphs.Length; i++) { positions[i] = new SKPoint(advance, 0); advance += widths[i]; }
            return new ShapedRun(glyphs, positions, advance);
        }

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        lock (Shapers) hbFont.Shape(buffer);

        var infos = buffer.GlyphInfos;
        var pos = buffer.GlyphPositions;
        var scale = font.Size / ShapeScale;
        var result = new ushort[infos.Length];
        var points = new SKPoint[infos.Length];
        var x = 0f;
        for (var i = 0; i < infos.Length; i++)
        {
            result[i] = (ushort)infos[i].Codepoint;
            points[i] = new SKPoint(x + pos[i].XOffset * scale, -pos[i].YOffset * scale);
            x += pos[i].XAdvance * scale;
        }
        return new ShapedRun(result, points, x);
    }

    private static HarfBuzzSharp.Font? ShaperFor(SKTypeface typeface)
    {
        lock (Shapers)
        {
            if (Shapers.TryGetValue(typeface.Handle, out var cached)) return cached;
            HarfBuzzSharp.Font? font = null;
            try
            {
                using var stream = typeface.OpenStream(out var index);
                if (stream is not null)
                {
                    // Copy the font file (an SKStreamAsset, not a System.IO.Stream) into native
                    // memory that the HarfBuzz blob owns and frees when HarfBuzz is done with
                    // it -- the face/font keep referencing this memory after this method.
                    var length = stream.Length;
                    var memory = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(length);
                    var read = stream.Read(memory, length);
                    using var blob = new HarfBuzzSharp.Blob(memory, read, HarfBuzzSharp.MemoryMode.ReadOnly,
                        () => System.Runtime.InteropServices.Marshal.FreeCoTaskMem(memory));
                    using var face = new HarfBuzzSharp.Face(blob, index);
                    font = new HarfBuzzSharp.Font(face);
                    font.SetScale(ShapeScale, ShapeScale);
                    font.SetFunctionsOpenType();
                }
            }
            catch
            {
                font = null;
            }
            Shapers[typeface.Handle] = font!;
            return font;
        }
    }

    private static void DrawTextRight(SKCanvas canvas, string text, float right, float y, SKFont font, SKColor color) =>
        DrawText(canvas, text, right - Measure(text, font), y, font, color);

    private static float Measure(string text, SKFont font) => Runs(text, font).Sum(r => Shape(r.Text, r.Font).Width);

    /// <summary>Shortens text with "..." to fit the width.</summary>
    private static string Fit(string text, float width, SKFont font)
    {
        if (string.IsNullOrEmpty(text) || Measure(text, font) <= width) return text;
        const string ellipsis = "...";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Measure(text[..mid] + ellipsis, font) <= width) lo = mid; else hi = mid - 1;
        }
        return lo == 0 ? ellipsis : text[..lo].TrimEnd() + ellipsis;
    }

    /// <summary>Splits text into runs the main font can draw and runs that need a
    /// fallback font (matched per character by the system font manager).</summary>
    private static IEnumerable<(string Text, SKFont Font)> Runs(string text, SKFont font)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var typeface = font.Typeface ?? SKTypeface.Default;
        var start = 0;
        SKFont? current = null;
        for (var i = 0; i < text.Length;)
        {
            var codepoint = char.ConvertToUtf32(text, i);
            var step = char.IsSurrogatePair(text, i) ? 2 : 1;
            // Combining marks / joiners stay with the run they modify so HarfBuzz can
            // shape the cluster (otherwise they render on dotted circles).
            var category = char.GetUnicodeCategory(text, i);
            var joins = current is not null && (category is System.Globalization.UnicodeCategory.NonSpacingMark
                or System.Globalization.UnicodeCategory.SpacingCombiningMark
                or System.Globalization.UnicodeCategory.EnclosingMark
                || codepoint is 0x200C or 0x200D);
            var runFont = joins ? current!
                : typeface.ContainsGlyph(codepoint) ? font
                : FallbackFor(codepoint, font);
            if (current is not null && !ReferenceEquals(runFont, current))
            {
                yield return (text[start..i], current);
                start = i;
            }
            current = runFont;
            i += step;
        }
        if (current is not null) yield return (text[start..], current);
    }

    // One SKFont per (fallback family, style, size), so consecutive characters of the
    // same script share a font object and form one run that HarfBuzz shapes as a whole.
    private static readonly Dictionary<(string, bool, float), SKFont> FallbackFonts = new();

    private static SKFont FallbackFor(int codepoint, SKFont font)
    {
        var bold = font.Typeface?.FontStyle.Weight >= (int)SKFontStyleWeight.SemiBold;
        var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;
        var typeface = SKFontManager.Default.MatchCharacter(null, style, null, codepoint);
        if (typeface is null) return font;
        lock (FallbackFonts)
        {
            var key = (typeface.FamilyName ?? "", bold, font.Size);
            if (FallbackFonts.TryGetValue(key, out var cached))
            {
                typeface.Dispose();
                return cached;
            }
            var result = new SKFont(typeface, font.Size) { Subpixel = true };
            FallbackFonts[key] = result;
            return result;
        }
    }

    private static SKTypeface LoadTypeface(SKFontStyle style)
    {
        foreach (var family in new[] { "Segoe UI", "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans" })
        {
            var typeface = SKTypeface.FromFamilyName(family, style);
            if (typeface is not null && string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                return typeface;
            typeface?.Dispose();
        }
        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }

    /// <summary>Font cache per size, so a long report doesn't create thousands of fonts.</summary>
    private sealed class Fonts
    {
        private readonly SKTypeface _regular;
        private readonly SKTypeface _bold;
        private readonly Dictionary<(float, bool), SKFont> _cache = new();

        public Fonts(SKTypeface regular, SKTypeface bold)
        {
            _regular = regular;
            _bold = bold;
        }

        public SKFont Regular(float size) => Get(size, false);
        public SKFont Bold(float size) => Get(size, true);

        private SKFont Get(float size, bool bold)
        {
            if (!_cache.TryGetValue((size, bold), out var font))
            {
                font = new SKFont(bold ? _bold : _regular, size) { Subpixel = true };
                _cache[(size, bold)] = font;
            }
            return font;
        }
    }
}
