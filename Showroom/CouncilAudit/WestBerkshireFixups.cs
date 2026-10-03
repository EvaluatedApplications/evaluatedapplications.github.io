// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\WestBerkshireFixups.cs, re-synced 2026-10-03 (Session 25/26 sync): NEW: West Berkshire synthetic row id + leading-blank-row skip.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

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

    public static List<string[]> AddSyntheticRowId(List<string[]> table)
    {
        if (table.Count == 0) return table;
        var result = new List<string[]>(table.Count) { table[0].Append("RowRef").ToArray() };
        for (int r = 1; r < table.Count; r++)
            result.Add(table[r].Append(r.ToString(CultureInfo.InvariantCulture)).ToArray());
        return result;
    }
}
