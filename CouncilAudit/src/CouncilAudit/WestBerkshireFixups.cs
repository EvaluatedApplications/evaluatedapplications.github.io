using System.Globalization;

namespace CouncilAudit;

/// <summary>
/// West Berkshire-specific fixup (council #5, onboarded 2026-10-03): this council
/// publishes NO transaction/invoice/voucher reference of any kind - its published
/// header is exactly "Service | Expenditure category | Narrative | Date | Net amount
/// | Supplier name", confirmed by hand against 29 real monthly .xlsx files
/// (2024-04 through 2026-08). Every other onboarded council has SOME field that at
/// least plays the role of a transaction id, even when it's the wrong shape for a
/// different check (Reading's bundled "Payment Number", Bracknell's batch-run
/// "TransNo") - West Berkshire has none at all. <see cref="AuditEngine.MapRows"/>
/// requires a TransactionId column name to look up; leaving it unmapped would make
/// every row share the same blank id (collapsing an entire file into one bogus
/// "transaction"), which is worse than useless. Adding a SYNTHETIC per-row-unique
/// column is the honest fix: it lets the engine run without fabricating a false
/// shared reference, and the one-row-per-synthetic-id shape means Schedule A
/// (single-row groups, already structurally near-empty here - see KnownQuirks, no
/// VAT split) and Schedule D (needs a REAL shared reference spanning >1 payee/date to
/// mean anything) both come out structurally empty BY CONSTRUCTION, not as a finding
/// about this council's spending - stated plainly wherever this council's Schedule
/// A/D output is shown. Schedule B (repeated same-supplier/amount/description/date
/// payments) does NOT depend on TransactionId at all and is the one schedule that
/// stays meaningful for this council.
/// </summary>
public static class WestBerkshireFixups
{
    /// <summary>
    /// 3 of the 29 files checked this session (2025-12, 2026-06, 2026-07) have ONE fully
    /// blank decorative row before the real header - confirmed by hand: row 0 is 6 empty
    /// cells, row 1 is the real "Service | Expenditure category | Narrative | Date | Net
    /// amount | Supplier name" header, row 2+ is real data. Every other file checked
    /// (2024-04 through 2025-11, 2026-01 through 2026-05) has the real header at row 0
    /// with no leading blank row at all - this is a genuine inconsistency in the
    /// council's own export process, not a single one-off. Detected structurally (any
    /// number of leading fully-blank rows, not hardcoded to exactly 1), a no-op on a
    /// file that already starts with the real header.
    /// </summary>
    public static List<string[]> SkipLeadingBlankHeaderRow(List<string[]> table)
    {
        int start = 0;
        while (start < table.Count && table[start].All(c => string.IsNullOrWhiteSpace(c))) start++;
        return start == 0 ? table : table.Skip(start).ToList();
    }

    /// <summary>
    /// Session 52 (raw-versus-published reconciliation): the row number now goes in the RowRef column itself. It used to be appended
    /// after the row's own last cell, so a row with fewer cells than the header (West Berkshire's blank trailing rows: 1,113 in six
    /// 2025-26 files) carried its number under "Supplier name"; such a row was dropped by the mapping without a word, and would have
    /// been read as a payment to a payee called "4053". A fully blank row is left blank (counted and dropped as a blank row); a row
    /// with more cells than the header keeps them after RowRef, unchanged.
    /// </summary>
    public static List<string[]> AddSyntheticRowId(List<string[]> table)
    {
        if (table.Count == 0) return table;
        int w = table[0].Length;
        var result = new List<string[]>(table.Count) { table[0].Append("RowRef").ToArray() };
        for (int r = 1; r < table.Count; r++)
        {
            var src = table[r];
            if (src.All(string.IsNullOrWhiteSpace)) { result.Add(src); continue; }
            var row = new string[Math.Max(src.Length, w) + 1];
            for (int c = 0; c < row.Length; c++) row[c] = "";
            for (int c = 0; c < Math.Min(src.Length, w); c++) row[c] = src[c];
            // Session 53 (checklist C08): the row number is the scanner's, not the council's. It carries the same placeholder text every
            // other unnumbered council uses, so no page, check or reader can take it for a published transaction number.
            row[w] = "(no number published) " + r.ToString(CultureInfo.InvariantCulture);
            for (int c = w; c < src.Length; c++) row[c + 1] = src[c];
            result.Add(row);
        }
        return result;
    }
}
