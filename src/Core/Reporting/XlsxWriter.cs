using System.IO.Compression;
using System.Security;
using System.Text;

namespace GozcuStandalone.Reporting;

// Writes a plain .xlsx workbook (one sheet, bold frozen header, filter, sized columns) with no extra libraries.
public static class XlsxWriter
{
    public static void Write(string path, string sheetName, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        Add(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);

        Add(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        Add(zip, "xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <sheets><sheet name="{Escape(SafeSheetName(sheetName))}" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);

        Add(zip, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);

        Add(zip, "xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
            <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE2E8F0"/><bgColor indexed="64"/></patternFill></fill></fills>
            <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
            <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
            <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs>
            <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """);

        var sheet = new StringBuilder();
        sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sheet.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sheet.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");

        sheet.Append("<cols>");
        for (int c = 0; c < headers.Count; c++)
        {
            int width = Math.Max(headers[c].Length, rows.Count == 0 ? 0 : rows.Max(r => c < r.Length ? r[c].Length : 0));
            sheet.Append($"""<col min="{c + 1}" max="{c + 1}" width="{Math.Clamp(width + 3, 8, 60)}" customWidth="1"/>""");
        }
        sheet.Append("</cols><sheetData>");

        sheet.Append("""<row r="1">""");
        for (int c = 0; c < headers.Count; c++)
            sheet.Append($"""<c r="{Col(c)}1" s="1" t="inlineStr"><is><t xml:space="preserve">{Escape(headers[c])}</t></is></c>""");
        sheet.Append("</row>");

        for (int r = 0; r < rows.Count; r++)
        {
            int rowNumber = r + 2;
            sheet.Append($"""<row r="{rowNumber}">""");
            for (int c = 0; c < headers.Count; c++)
            {
                var value = c < rows[r].Length ? rows[r][c] : "";
                if (value.Length == 0) continue;
                if (IsPlainInteger(value))
                    sheet.Append($"""<c r="{Col(c)}{rowNumber}"><v>{value}</v></c>""");
                else
                    sheet.Append($"""<c r="{Col(c)}{rowNumber}" t="inlineStr"><is><t xml:space="preserve">{Escape(value)}</t></is></c>""");
            }
            sheet.Append("</row>");
        }

        sheet.Append("</sheetData>");
        if (headers.Count > 0) sheet.Append($"""<autoFilter ref="A1:{Col(headers.Count - 1)}{Math.Max(1, rows.Count + 1)}"/>""");
        sheet.Append("</worksheet>");
        Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }

    // IDs and counts become real numbers; anything with leading zeros, dates or times stays text.
    private static bool IsPlainInteger(string v) =>
        v.Length is > 0 and <= 9 && v.All(char.IsAsciiDigit) && (v.Length == 1 || v[0] != '0');

    private static string Col(int index)
    {
        var s = "";
        for (index++; index > 0; index = (index - 1) / 26) s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }

    private static string Escape(string v) => SecurityElement.Escape(v) ?? "";

    private static string SafeSheetName(string name)
    {
        var clean = new string(name.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
        if (clean.Length == 0) clean = "Report";
        return clean.Length > 31 ? clean[..31] : clean;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }
}
