using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace InfoID.Application.Reporting;

/// <summary>
/// Writes a <see cref="ReportTable"/> as a real Excel workbook (.xlsx, Office Open XML)
/// using only the .NET zip classes -- no third-party package. One sheet:
///   title, organization, filters, generated time, the summary pairs,
///   then the table with a bold, frozen, auto-filtered header row.
/// Numbers are stored as numbers and dates as real Excel dates (sortable/filterable).
/// </summary>
public static class XlsxReportWriter
{
    // Style indexes into styles.xml cellXfs (see Styles below).
    private const int StyleDefault = 0, StyleTitle = 1, StyleHeader = 2, StyleDate = 3, StyleDateTime = 4, StyleBold = 5, StyleMuted = 6;

    public static void Write(ReportTable table, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Add(zip, "[Content_Types].xml", ContentTypes);
        Add(zip, "_rels/.rels", RootRels);
        Add(zip, "xl/workbook.xml", Workbook(SheetName(table.Title)));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
        Add(zip, "xl/styles.xml", Styles);
        Add(zip, "xl/worksheets/sheet1.xml", Sheet(table));
        Add(zip, "docProps/core.xml", Core(table));
    }

    public static byte[] ToBytes(ReportTable table)
    {
        using var ms = new MemoryStream();
        Write(table, ms);
        return ms.ToArray();
    }

    private static string Sheet(ReportTable table)
    {
        var sb = new StringBuilder();
        var colCount = Math.Max(1, table.Columns.Count);
        var row = 0;
        var headerRow = 0;

        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
          .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");

        // Header block height is needed for the frozen pane, so build rows first.
        var rows = new StringBuilder();
        void TextRow(string text, int style)
        {
            row++;
            rows.Append($"<row r=\"{row}\">").Append(TextCell(row, 0, text, style)).Append("</row>");
        }

        TextRow(table.Title, StyleTitle);
        if (!string.IsNullOrWhiteSpace(table.OrganizationName)) TextRow(table.OrganizationName!, StyleBold);
        if (!string.IsNullOrWhiteSpace(table.Subtitle)) TextRow(table.Subtitle, StyleMuted);
        TextRow($"Generated {table.GeneratedAt:dd MMM yyyy HH:mm} by InfoID", StyleMuted);

        if (table.Summary.Count > 0)
        {
            row++; // blank line
            foreach (var pair in table.Summary)
            {
                row++;
                rows.Append($"<row r=\"{row}\">")
                    .Append(TextCell(row, 0, pair.Key, StyleBold))
                    .Append(ValueCell(row, 1, pair.Value))
                    .Append("</row>");
            }
        }

        row += 2;
        headerRow = row;
        rows.Append($"<row r=\"{row}\">");
        for (var c = 0; c < table.Columns.Count; c++)
            rows.Append(TextCell(row, c, table.Columns[c].Header, StyleHeader));
        rows.Append("</row>");

        foreach (var values in table.Rows)
        {
            row++;
            rows.Append($"<row r=\"{row}\">");
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = c < values.Length ? values[c] : null;
                rows.Append(DataCell(row, c, value, table.Columns[c].Type));
            }
            rows.Append("</row>");
        }

        var lastRow = Math.Max(row, headerRow);
        sb.Append($"<dimension ref=\"A1:{Col(colCount - 1)}{lastRow}\"/>");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">")
          .Append($"<pane ySplit=\"{headerRow}\" topLeftCell=\"A{headerRow + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>")
          .Append("</sheetView></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        sb.Append("<cols>");
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var width = ColumnWidth(table, c);
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        }
        sb.Append("</cols>");

        sb.Append("<sheetData>").Append(rows).Append("</sheetData>");
        if (table.Columns.Count > 0)
            sb.Append($"<autoFilter ref=\"A{headerRow}:{Col(colCount - 1)}{lastRow}\"/>");
        sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static double ColumnWidth(ReportTable table, int c)
    {
        var col = table.Columns[c];
        var longest = col.Header.Length;
        foreach (var values in table.Rows.Take(500))
        {
            var text = ReportTable.FormatCell(c < values.Length ? values[c] : null, col.Type);
            if (text.Length > longest) longest = text.Length;
        }
        if (col.Type == ReportColumnType.DateTime) longest = Math.Max(longest, 17);
        if (col.Type == ReportColumnType.Date) longest = Math.Max(longest, 12);
        return Math.Clamp(longest + 2, 8, 60);
    }

    private static string DataCell(int row, int col, object? value, ReportColumnType type) => value switch
    {
        null => "",
        DateTime d => $"<c r=\"{Col(col)}{row}\" s=\"{(type == ReportColumnType.DateTime ? StyleDateTime : StyleDate)}\"><v>{ToOaDate(d, type)}</v></c>",
        int i => $"<c r=\"{Col(col)}{row}\"><v>{i.ToString(CultureInfo.InvariantCulture)}</v></c>",
        long l => $"<c r=\"{Col(col)}{row}\"><v>{l.ToString(CultureInfo.InvariantCulture)}</v></c>",
        double d => $"<c r=\"{Col(col)}{row}\"><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>",
        _ => TextCell(row, col, value.ToString() ?? "", StyleDefault),
    };

    /// <summary>Summary values are text like "1,234"; store plain numbers as numbers.</summary>
    private static string ValueCell(int row, int col, string value) =>
        long.TryParse(value.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? $"<c r=\"{Col(col)}{row}\"><v>{n.ToString(CultureInfo.InvariantCulture)}</v></c>"
            : TextCell(row, col, value, StyleDefault);

    private static string TextCell(int row, int col, string text, int style) =>
        $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"{(style == 0 ? "" : $" s=\"{style}\"")}><is><t xml:space=\"preserve\">{Xml(text)}</t></is></c>";

    private static string ToOaDate(DateTime d, ReportColumnType type)
    {
        var value = type == ReportColumnType.DateTime ? d.ToOADate() : d.Date.ToOADate();
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    /// <summary>0 -> A, 25 -> Z, 26 -> AA.</summary>
    private static string Col(int index)
    {
        var name = "";
        index++;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            name = (char)('A' + rem) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    /// <summary>Escapes XML and drops characters XML 1.0 cannot carry.</summary>
    private static string Xml(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20) clean.Append(ch);
        return SecurityElement.Escape(clean.ToString()) ?? "";
    }

    private static string SheetName(string title)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var name = new string(title.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (name.Length == 0) name = "Report";
        return name.Length > 31 ? name[..31] : name;
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
        "</Relationships>";

    private static string Workbook(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<sheets><sheet name=\"{Xml(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
        "</workbook>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // cellXfs: 0 default, 1 title (bold 14), 2 header (bold white on red, border),
    //          3 date, 4 date-time, 5 bold, 6 grey text.
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"dd mmm yyyy\"/><numFmt numFmtId=\"165\" formatCode=\"dd mmm yyyy hh:mm\"/></numFmts>" +
        "<fonts count=\"5\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><sz val=\"10\"/><color rgb=\"FF666666\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDA3025\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FF8E1B13\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"7\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static string Core(ReportTable table) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
        "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
        $"<dc:title>{Xml(table.Title)}</dc:title><dc:creator>InfoID</dc:creator>" +
        $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>" +
        "</cp:coreProperties>";
}

/// <summary>Writes a <see cref="ReportTable"/> as CSV (UTF-8 with BOM so Excel shows
/// non-English names correctly). Dates use ISO format so any program reads them.</summary>
public static class CsvReportWriter
{
    public static void Write(ReportTable table, Stream output)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true);
        writer.WriteLine(string.Join(",", table.Columns.Select(c => Escape(c.Header))));
        foreach (var values in table.Rows)
        {
            var cells = new List<string>(table.Columns.Count);
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = c < values.Length ? values[c] : null;
                cells.Add(Escape(value switch
                {
                    null => "",
                    DateTime d when table.Columns[c].Type == ReportColumnType.DateTime => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? "",
                }));
            }
            writer.WriteLine(string.Join(",", cells));
        }
    }

    private static string Escape(string value)
    {
        // Guard against spreadsheet formula injection from data (=, +, -, @ at the start).
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
