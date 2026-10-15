namespace CouncilAudit;

/// <summary>
/// Fixups the big cities' files need before column mapping (Session 30). Each one is named after a real row or header
/// found in a real file, with a regression case in CityFixupsTests.
/// </summary>
public static class CityFixups
{
    /// <summary>
    /// Leeds' monthly CSVs from April 2017 to March 2020 spell the payee column "Benificiary Name"; from April 2020 on
    /// it is "Beneficiary Name". Left alone, the mapping finds no supplier column in those 36 files and the engine skips
    /// them. The rename changes the header cell only, never a data cell.
    /// </summary>
    public static List<string[]> LeedsHeader(List<string[]> table)
    {
        if (table.Count == 0) return table;
        var header = table[0];
        for (int i = 0; i < header.Length; i++)
            if (header[i].Trim().TrimStart('﻿').Equals("Benificiary Name", StringComparison.OrdinalIgnoreCase))
            {
                var fixedHeader = (string[])header.Clone();
                fixedHeader[i] = "Beneficiary Name";
                var result = new List<string[]>(table) { [0] = fixedHeader };
                return result;
            }
        return table;
    }

    /// <summary>
    /// Session 31: Bradford's files (Bradford Data Hub, "Council expenditure greater than 500") carry four header layouts: the
    /// 2020-21 annual file ("Expenditure code, Expenditure category, Payment date"), the 2021 to 2024 quarterlies ("Expenditure
    /// Code, Expenditure Category, Payment Date, ... Supplier Name"), the 2025 files ("SupplierName") and from mid-2026 a file
    /// where "Expenditure Category" holds the CODE and "Expenditure Category Description" the text, with both "SupplierName" and
    /// "Supplier Name". One standard set: Payment Date, Expenditure Code, Expenditure Description, Supplier Name. No data cell
    /// changes.
    /// </summary>
    public static List<string[]> BradfordHeader(List<string[]> table)
    {
        if (table.Count == 0) return table;
        var h = table[0].Select(c => c.Trim().TrimStart('﻿')).ToArray();
        bool Has(string n) => h.Any(c => c.Equals(n, StringComparison.OrdinalIgnoreCase));
        bool hasDesc = Has("Expenditure Category Description");
        // Two payee-name columns ("SupplierName" and "Supplier Name", mid-2026 files): one of them is empty in the file. The standard
        // name goes to the column that actually holds names (more non-blank cells); the other becomes "Supplier Name (alt)".
        int iA = Array.FindIndex(h, c => c.Equals("SupplierName", StringComparison.OrdinalIgnoreCase));
        int iB = Array.FindIndex(h, c => c.Equals("Supplier Name", StringComparison.OrdinalIgnoreCase));
        bool swapNames = false;
        if (iA >= 0 && iB >= 0)
        {
            int nonA = table.Skip(1).Count(r => iA < r.Length && !string.IsNullOrWhiteSpace(r[iA]));
            int nonB = table.Skip(1).Count(r => iB < r.Length && !string.IsNullOrWhiteSpace(r[iB]));
            swapNames = nonA > nonB;
        }
        for (int i = 0; i < h.Length; i++)
        {
            string c = h[i];
            if (c.Equals("Payment date", StringComparison.OrdinalIgnoreCase)) h[i] = "Payment Date";
            else if (c.Equals("Expenditure code", StringComparison.OrdinalIgnoreCase)) h[i] = "Expenditure Code";
            else if (c.Equals("Expenditure Category Description", StringComparison.OrdinalIgnoreCase)) h[i] = "Expenditure Description";
            else if (c.Equals("Expenditure category", StringComparison.OrdinalIgnoreCase)) h[i] = hasDesc ? "Expenditure Code" : "Expenditure Description";
            else if (c.Equals("SupplierName", StringComparison.OrdinalIgnoreCase)) h[i] = iB < 0 || swapNames ? "Supplier Name" : "Supplier Name (alt)";
            else if (c.Equals("Supplier Name", StringComparison.OrdinalIgnoreCase) && iA >= 0 && swapNames) h[i] = "Supplier Name (alt)";
        }
        var result = new List<string[]>(table) { [0] = h };
        return result;
    }

    /// <summary>
    /// Session 31: header normalisation plus the one data repair Bradford needs. Whole files (2025-04, 2025-05, 2026-02 and the
    /// repeated July to September 2025 file named August) publish the payee NAME column empty on every row while the Supplier
    /// Number is filled. A blank payee name would make unrelated suppliers one "payee" for Schedule B (groups of 500+ lines), so a
    /// row with no name and a Supplier Number is given the label "(no name published) N01651": the published number as the payee
    /// identity, no name inferred from any other file. A row with neither stays blank.
    /// </summary>
    public static List<string[]> BradfordPrepare(List<string[]> table)
    {
        var t = BradfordHeader(table);
        if (t.Count == 0) return t;
        int iName = Array.FindIndex(t[0], c => c == "Supplier Name");
        int iNum = Array.FindIndex(t[0], c => c.Equals("Supplier Number", StringComparison.OrdinalIgnoreCase));
        if (iName < 0 || iNum < 0) return t;
        var result = new List<string[]>(t.Count) { t[0] };
        for (int i = 1; i < t.Count; i++)
        {
            var r = t[i];
            if (iName < r.Length && iNum < r.Length && string.IsNullOrWhiteSpace(r[iName]) && !string.IsNullOrWhiteSpace(r[iNum]))
            {
                r = (string[])r.Clone();
                r[iName] = "(no name published) " + r[iNum].Trim();
            }
            result.Add(r);
        }
        return result;
    }

    /// <summary>
    /// Session 31: Sheffield has published under five header layouts since 2017 (Data Mill North "Council spend over 250").
    /// January to July 2017 name the columns "Organisational Unit, Expense Code, GL Date, Document No., Invoice No., Invoiced
    /// Value, Supplier Name, Address Book No., Proclass Details Level 1"; August 2017 to March 2019 use "GL Date, Invoice No.,
    /// Value, Supplier" (and one file calls the first column "Company"); from April 2019 "Certified Date, Supplier Reference,
    /// Value, Supplier", one file spells "Objective Code" for "Object Code", another "Suplier Number", another "Category &amp;
    /// Description". Each header cell is renamed to ONE standard name; no data cell changes except that every cell is trimmed
    /// (the 2017 files pad names with spaces to a fixed width). The Document No. column, present only in 2017-01 to 2017-07, keeps
    /// its own name and travels in OtherColumns.
    /// </summary>
    public static List<string[]> SheffieldHeader(List<string[]> table)
    {
        if (table.Count == 0) return table;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Certified Date"] = "Payment Date", ["GL Date"] = "Payment Date",
            ["Invoiced Value"] = "Value",
            ["Supplier Name"] = "Supplier",
            ["Suplier Number"] = "Supplier No", ["Address Book No."] = "Supplier No",
            ["Invoice No."] = "Invoice No", ["Invoice No"] = "Invoice No", ["Supplier Reference"] = "Invoice No",
            ["Objective Code"] = "Object Code", ["Expense Code"] = "Object Code",
            ["Objective Code Description"] = "Object Code Description", ["Expense Code Description"] = "Object Code Description",
            ["Category & Description"] = "Category Description", ["Thomson Classification Description"] = "Category Description",
            ["Proclass Details Level 1"] = "Category",
            ["Organisational Unit"] = "Portfolio",
            ["Company"] = "Body Name",
            ["Business Unit No."] = "Organisation Code",
            ["Business Unit Description"] = "Org Code Description",
        };
        var header = table[0].Select(h =>
        {
            string t = h.Trim().TrimStart('﻿');
            return map.TryGetValue(t, out var std) ? std : t;
        }).ToArray();
        var result = new List<string[]>(table.Count) { header };
        for (int i = 1; i < table.Count; i++) result.Add(table[i].Select(c => c.Trim()).ToArray());
        return result;
    }
}
