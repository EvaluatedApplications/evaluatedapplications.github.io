// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\XlsxReader.cs, re-synced 2026-10-03 (Session 25/26 sync): verbatim upstream (includes the office-document relationship-namespace fix first made here).
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

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

    public static List<string[]> Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sharedStrings = ReadSharedStrings(zip);
        string sheetPath = FindFirstSheetPath(zip);

        var entry = zip.GetEntry(sheetPath) ?? throw new InvalidDataException("xlsx: no worksheet found");
        using var sheetStream = entry.Open();
        var doc = XDocument.Load(sheetStream);

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

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
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
        var wbDoc = XDocument.Load(wbStream);
        var firstSheet = wbDoc.Root!.Element(Main + "sheets")!.Elements(Main + "sheet").First();
        string rId = (string)firstSheet.Attribute(Rel + "id")!;

        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("xlsx: no workbook.xml.rels");
        using var relsStream = relsEntry.Open();
        var relsDoc = XDocument.Load(relsStream);
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
