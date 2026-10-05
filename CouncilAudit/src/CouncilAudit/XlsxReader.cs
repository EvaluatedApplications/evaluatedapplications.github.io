using System.IO.Compression;
using System.Xml.Linq;

namespace CouncilAudit;

/// <summary>
/// Minimal .xlsx reader: just enough to pull the first worksheet out as rows of
/// strings, the same shape <see cref="CsvReader"/> produces. .xlsx is a zip of XML
/// parts, and <see cref="ZipArchive"/> over a <see cref="MemoryStream"/> works inside
/// a Blazor WebAssembly page, so this needs no extra NuGet package and no file access.
/// Deliberately does not handle formulas, styles, merged cells or multiple sheets -
/// council "spend over £500" files are flat data exports and have never needed any of
/// that in practice (checked: Wokingham's one .xlsx year, FY2023-24).
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    // BUG FIX (2026-10-03, caught by the Showroom owner vendoring this file against a real Excel
    // .xlsx, confirmed against Wokingham's FY2023-24 file): a <sheet r:id="..."> attribute lives in
    // the OFFICE-DOCUMENT relationships namespace, not the PACKAGE relationships namespace - those
    // are two different real OOXML namespace URIs. Reading it against the wrong one silently returns
    // a null XAttribute (the explicit string-cast operator on a null XAttribute returns null rather
    // than throwing), so rId ends up null and FindFirstSheetPath fails with "Sequence contains no
    // matching element" on every real Excel-produced file.
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";


    /// <summary>Loads an XML part. A workbook saved as "Strict Open XML" (Wirral's March 2023 file) uses the purl.oclc.org namespaces instead of the usual ones;
    /// they are rewritten to the usual ones before parsing so one set of element names serves both. Nothing else changes.</summary>
    private static XDocument LoadXml(Stream s)
    {
        using var sr = new StreamReader(s);
        string t = sr.ReadToEnd();
        if (t.Contains("purl.oclc.org/ooxml"))
            t = t.Replace("http://purl.oclc.org/ooxml/spreadsheetml/main", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                 .Replace("http://purl.oclc.org/ooxml/officeDocument/relationships", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        return XDocument.Parse(t);
    }
    public static List<string[]> Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sharedStrings = ReadSharedStrings(zip);
        string sheetPath = FindFirstSheetPath(zip);

        var entry = zip.GetEntry(sheetPath) ?? throw new InvalidDataException("xlsx: no worksheet found");
        using var sheetStream = entry.Open();
        var doc = LoadXml(sheetStream);

        var rows = new List<string[]>();
        var sheetData = doc.Root?.Element(Main + "sheetData");
        if (sheetData is null) return rows;

        foreach (var rowEl in sheetData.Elements(Main + "row"))
        {
            var cells = rowEl.Elements(Main + "c").ToList();
            int maxCol = 0;
            var cellValues = new Dictionary<int, string>();
            foreach (var cellEl in cells)
            {
                string? refAttr = (string?)cellEl.Attribute("r");
                int colIdx = refAttr is not null ? ColumnIndexFromRef(refAttr) : cellValues.Count;
                string type = (string?)cellEl.Attribute("t") ?? "n";
                var vEl = cellEl.Element(Main + "v");
                string raw = vEl?.Value ?? "";
                string value = type switch
                {
                    "s" => int.TryParse(raw, out var ssIdx) && ssIdx >= 0 && ssIdx < sharedStrings.Count
                        ? sharedStrings[ssIdx] : "",
                    "inlineStr" => cellEl.Element(Main + "is")?.Element(Main + "t")?.Value ?? "",
                    "str" => raw,
                    "b" => raw == "1" ? "TRUE" : "FALSE",
                    _ => raw, // numeric (incl. date serials) - caller's ParseMoney/ParseDate handles serials
                };
                cellValues[colIdx] = value;
                if (colIdx > maxCol) maxCol = colIdx;
            }
            var arr = new string[maxCol + 1];
            foreach (var (idx, val) in cellValues) arr[idx] = val;
            for (int k = 0; k < arr.Length; k++) arr[k] ??= "";
            rows.Add(arr);
        }
        return rows;
    }

    /// <summary>Session 53 (checklist C02): a computed spreadsheet cell stores 17 digits ("-6680.1000000000004"), which the sheet shows as -6680.1.
    /// A cell that long is rewritten to the 15 significant digits a spreadsheet shows (G15); anything shorter is returned unchanged.</summary>
    public static string TidyNumber(string s)
    {
        if (s.Length < 16 || !double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return s;
        string t = d.ToString("G15", System.Globalization.CultureInfo.InvariantCulture);
        return t.Contains('E') ? s : t;
    }

    /// <summary>Tidies the money columns (by header name) of a table read from a workbook, in place. Only these columns: ids and dates are left as published.</summary>
    public static void TidyMoneyCells(List<string[]> table, params string?[] columns)
    {
        if (table.Count == 0) return;
        var idx = columns.Where(c => c is not null)
            .Select(c => Array.FindIndex(table[0], h => h.Trim().TrimStart('﻿').Equals(c, StringComparison.OrdinalIgnoreCase)))
            .Where(i => i >= 0).Distinct().ToList();
        for (int r = 1; r < table.Count; r++)
            foreach (int j in idx)
                if (j < table[r].Length) table[r][j] = TidyNumber(table[r][j]);
    }

    /// <summary>
    /// Every worksheet by name (Session 29: the government's older Capital Outturn Return files are multi-sheet
    /// .xlsx workbooks, and the sheet that carries the all-services total is not the first). Same cell rules as
    /// <see cref="Parse"/>.
    /// </summary>
    public static Dictionary<string, List<string[]>> ParseAllSheets(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var sharedStrings = ReadSharedStrings(zip);
        var wbDoc = LoadXml(zip.GetEntry("xl/workbook.xml")!.Open());
        var relsDoc = LoadXml(zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
        var result = new Dictionary<string, List<string[]>>();
        foreach (var sheet in wbDoc.Root!.Element(Main + "sheets")!.Elements(Main + "sheet"))
        {
            string name = (string?)sheet.Attribute("name") ?? "";
            string rId = (string)sheet.Attribute(Rel + "id")!;
            string target = (string)relsDoc.Root!.Elements().First(e => (string)e.Attribute("Id")! == rId).Attribute("Target")!;
            string path = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
            var entry = zip.GetEntry(path);
            if (entry is null) continue;
            var doc = LoadXml(entry.Open());
            var rows = new List<string[]>();
            var sheetData = doc.Root?.Element(Main + "sheetData");
            if (sheetData is not null)
                foreach (var rowEl in sheetData.Elements(Main + "row"))
                {
                    int maxCol = 0;
                    var cellValues = new Dictionary<int, string>();
                    foreach (var cellEl in rowEl.Elements(Main + "c"))
                    {
                        string? refAttr = (string?)cellEl.Attribute("r");
                        int colIdx = refAttr is not null ? ColumnIndexFromRef(refAttr) : cellValues.Count;
                        string type = (string?)cellEl.Attribute("t") ?? "n";
                        string raw = cellEl.Element(Main + "v")?.Value ?? "";
                        cellValues[colIdx] = type switch
                        {
                            "s" => int.TryParse(raw, out var ss) && ss >= 0 && ss < sharedStrings.Count ? sharedStrings[ss] : "",
                            "inlineStr" => cellEl.Element(Main + "is")?.Element(Main + "t")?.Value ?? "",
                            "b" => raw == "1" ? "TRUE" : "FALSE",
                            _ => raw,
                        };
                        if (colIdx > maxCol) maxCol = colIdx;
                    }
                    var arr = new string[maxCol + 1];
                    foreach (var (idx, val) in cellValues) arr[idx] = val;
                    for (int k = 0; k < arr.Length; k++) arr[k] ??= "";
                    rows.Add(arr);
                }
            result[name] = rows;
        }
        return result;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;
        using var stream = entry.Open();
        var doc = LoadXml(stream);
        foreach (var si in doc.Root!.Elements(Main + "si"))
        {
            // Concatenate all <t> runs (handles rich text split across <r><t>).
            var text = string.Concat(si.Descendants(Main + "t").Select(t => t.Value));
            list.Add(text);
        }
        return list;
    }

    private static string FindFirstSheetPath(ZipArchive zip)
    {
        var wbEntry = zip.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("xlsx: no workbook.xml");
        using var wbStream = wbEntry.Open();
        var wbDoc = LoadXml(wbStream);
        var firstSheet = wbDoc.Root!.Element(Main + "sheets")!.Elements(Main + "sheet").First();
        string rId = (string)firstSheet.Attribute(Rel + "id")!;

        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("xlsx: no workbook.xml.rels");
        using var relsStream = relsEntry.Open();
        var relsDoc = LoadXml(relsStream);
        var rel = relsDoc.Root!.Elements().First(e => (string)e.Attribute("Id")! == rId);
        string target = (string)rel.Attribute("Target")!;
        return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
    }

    private static int ColumnIndexFromRef(string cellRef)
    {
        int col = 0;
        foreach (char c in cellRef)
        {
            if (!char.IsLetter(c)) break;
            col = col * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return col - 1;
    }
}
