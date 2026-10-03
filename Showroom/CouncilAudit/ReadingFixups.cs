// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\ReadingFixups.cs, copied
// 2026-10-03 (new file, added alongside the DebtCharge-fix re-sync so CouncilDbBuilder can
// apply the same header/layout fixups the full Reading back-catalogue needs).
// CouncilAudit engine by the EA virtual-customer agent. Do not edit the source repo from here;
// future engine changes happen upstream and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>
/// Reading-specific header/layout repairs (Session 17 findings, pulling the full
/// FY2020-21..FY2025-26 back-catalogue) and the general-purpose "bad file" guard
/// (Session 17, named for the specific corruption it caught: reading_2021-05.xlsx
/// "May21-Revised"). Each has its own unit test against a synthetic byte sequence in
/// the upstream CouncilAudit.Tests project - see BASELINE.md Section 5.
/// </summary>
public static class ReadingFixups
{
    /// <summary>
    /// Two early files (2020-Q3, 2021-07) use the plain column name "Voucher Number",
    /// predating the "(Internal Classification)" suffix every later file carries - a
    /// naming-convention change, not a typo. Two files have a genuine one-off typo in
    /// the council's own header text, confirmed by hand against the raw bytes: "Voucher
    /// Number (Jnternal Classification)" (2021-11, J for I) and "Voucher Number
    /// Iinternal Classification)" (2022-11, missing open paren). 2020-Q3 also spells its
    /// last column "Decsription" instead of "Directorate". Each fixup is a named,
    /// hand-confirmed string substitution, not a generic fuzzy match - a wrong guess
    /// here would silently remap an unrelated column.
    /// </summary>
    public static readonly (string from, string to)[] HeaderFixups = new[]
    {
        ("Voucher Number", "Voucher Number (Internal Classification)"),
        ("Voucher Number (Jnternal Classification)", "Voucher Number (Internal Classification)"),
        ("Voucher Number Iinternal Classification)", "Voucher Number (Internal Classification)"),
        ("Invoice Type", "Invoice Type (Internal Classification)"),
        ("Decsription", "Directorate"),
    };

    /// <summary>Mutates <paramref name="table"/>'s header row (row 0) in place, applying
    /// each matching fixup above. No-op if the table is empty.</summary>
    public static void ApplyHeaderFixups(IReadOnlyList<string[]> table, (string from, string to)[] fixups)
    {
        if (table.Count == 0) return;
        var header = table[0];
        for (int i = 0; i < header.Length; i++)
        {
            var trimmed = header[i].Trim().TrimStart('﻿');
            foreach (var (from, to) in fixups)
            {
                if (trimmed.Equals(from, StringComparison.OrdinalIgnoreCase)) { header[i] = to; break; }
            }
        }
    }

    /// <summary>
    /// The 7 early Reading .xlsx files (2020-Q1/Q2, 2021-Q4, 2021-04/05/06/09) have 4
    /// blank decorative rows before the real header, and the real header itself is split
    /// across TWO physical rows: row 5 carries the main column names ("Voucher Number",
    /// "Supplier Type", "Invoice Type", ...), row 6 carries a qualifier ("(Internal
    /// Classification)") under 3 of those columns only, mostly-blank otherwise - an
    /// Excel-template merged-cell/line-wrap artifact, not a second data row. Treating
    /// row 1 as header (what a naive xlsx read does) silently returns an all-blank
    /// header and 2 bogus near-blank "data" rows per file. Detected structurally (not
    /// hardcoded to exactly 4 blank rows or row index 6), so it survives a slightly
    /// different blank-row count without being re-checked by hand again.
    /// </summary>
    public static List<string[]> FixXlsxLayout(List<string[]> table)
    {
        int start = 0;
        while (start < table.Count && table[start].All(c => string.IsNullOrWhiteSpace(c))) start++;
        var trimmed = table.Skip(start).ToList();
        if (trimmed.Count > 1)
        {
            var second = trimmed[1];
            var nonBlank = second.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (nonBlank.Count > 0 && nonBlank.All(c => c.Trim().StartsWith("(")))
                trimmed.RemoveAt(1); // header-qualifier row, not real data
        }
        return trimmed;
    }

    /// <summary>
    /// The bad-file guard (Session 17, named for reading_2021-05.xlsx "May21-Revised"):
    /// a file whose header doesn't match its own data columns (that file's header row
    /// references a shared-string index that doesn't match its own data columns - the
    /// "Payment Number" column's data cells actually contain supplier names) must not
    /// silently contribute blank/zero rows to the aggregate. <see cref="AuditEngine.MapRows"/>
    /// only WARNS when a critical column name isn't found in the header at all - it does
    /// not refuse to run - so the caller must check whether MapRows added a NEW warning
    /// for this file and drop the whole file if so, rather than trusting MapRows' own
    /// row output. Returns true if the file should be INCLUDED (no critical-column
    /// mapping failure), false if it should be dropped.
    /// </summary>
    public static bool IsFileMappingUsable(List<string> warnings, int warningCountBeforeMapping) =>
        warnings.Count == warningCountBeforeMapping;
}
