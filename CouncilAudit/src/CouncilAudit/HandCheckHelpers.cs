namespace CouncilAudit;

/// <summary>
/// Hand-check helpers used by CouncilAudit.Cli's seeded-sample validation. Library-level
/// (not CLI local functions), so they're unit-testable without file I/O - same reasoning as
/// ReadingFixups.cs (Session 18).
///
/// Session 19: this file retires `ConfirmedInRawDetailed`, the function every earlier
/// session's Schedule A/B hand-check used. It worked by re-scanning the WHOLE raw table
/// for every row whose (TransactionId, Supplier) TEXT matched, then aggregating over every
/// match found that way. That shape produced the same real failure three separate times:
/// Schedule D's hand-check double-counted trans 4378111 (Session 18, fixed locally by
/// `ConfirmedScheduleDMember`), the Reading-&gt;Wokingham/Bracknell Forest crossref hand-open
/// hit the identical bug the same session (fixed locally by `ConfirmedExactLineAmongCandidates`
/// in Program.cs), and BASELINE.md Section 7 named a third, still-theoretical case for
/// Merton (two unrelated same-numbered invoices from different transactions could be summed
/// together if they also share a supplier name). Three occurrences of one root cause is a
/// sign to retire the shared root, not patch a fourth call site - this is that retirement.
///
/// Both replacements below look up the EXACT raw row position(s) the engine itself already
/// recorded for this classified row/group when it built it (`ScheduleARow.MemberRowIndexes`,
/// `ScheduleBGroup.MemberRowIndexes`) - never re-discovering "which raw rows belong to this
/// group" by a second, looser text match over the whole table.
/// </summary>
public static class HandCheckHelpers
{
    /// <summary>
    /// Re-derives a Schedule A row's Net/Gross/Description from exactly the raw row
    /// positions (<see cref="ScheduleARow.MemberRowIndexes"/>) that
    /// <see cref="AuditEngine.Run"/> itself grouped to build this row, and compares them to
    /// what the classified row claims. Returns null when fully confirmed, or a short
    /// human-readable mismatch reason otherwise ("not found"/"out of range" and a field
    /// mismatch are different failure modes, reported distinctly, same discipline as the
    /// function this replaces).
    /// </summary>
    public static string? ConfirmedScheduleAExactLines(
        IReadOnlyList<string[]> table, ColumnMapping map, ScheduleARow a, bool repeatedInvoiceTotal)
    {
        if (table.Count == 0) return "raw table is empty";
        var rowIndexes = a.MemberRowIndexes;
        if (rowIndexes.Count == 0)
            return "no member row indexes recorded for this group (built before Session 19's MemberRowIndexes field existed?)";

        var header = table[0].Select(h => h.Trim().TrimStart('﻿')).ToArray();
        int Idx(string? name) => name is null ? -1 : Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int iTrans = Idx(map.TransactionId), iSupplier = Idx(map.Supplier), iNet = Idx(map.Net),
            iGross = Idx(map.Gross), iVatAmt = Idx(map.VatAmount), iDesc = Idx(map.Description);

        decimal netSum = 0m, grossSum = 0m, firstGross = 0m;
        string? firstDesc = null;
        for (int k = 0; k < rowIndexes.Count; k++)
        {
            int rowIdx = rowIndexes[k];
            int rawRowNum = rowIdx + 1; // MapRows: RowIndexInSource = r - 1, r starts at 1 (table[0] is header)
            if (rawRowNum < 1 || rawRowNum >= table.Count)
                return $"member row index {rowIdx} out of range for raw table of {table.Count - 1} data rows";
            var row = table[rawRowNum];
            if (iTrans >= 0 && iTrans < row.Length && row[iTrans].Trim() != a.TransactionId)
                return $"transaction id mismatch at raw row {rawRowNum}: raw \"{row[iTrans].Trim()}\" vs classified \"{a.TransactionId}\"";
            if (iSupplier >= 0 && iSupplier < row.Length && row[iSupplier].Trim() != a.Supplier)
                return $"supplier mismatch at raw row {rawRowNum}: raw \"{row[iSupplier].Trim()}\" vs classified \"{a.Supplier}\"";

            decimal rowGross = iGross >= 0 && iGross < row.Length ? AuditEngine.ParseMoney(row[iGross]) : 0m;
            decimal rowNet = iNet >= 0 && iNet < row.Length ? AuditEngine.ParseMoney(row[iNet])
                : (iVatAmt >= 0 && iVatAmt < row.Length ? rowGross - AuditEngine.ParseMoney(row[iVatAmt]) : rowGross);
            netSum += rowNet;
            grossSum += rowGross;
            if (k == 0) { firstGross = rowGross; firstDesc = iDesc >= 0 && iDesc < row.Length ? row[iDesc].Trim() : null; }
        }

        if (Math.Abs(netSum - a.Net) > 0.01m)
            return $"net mismatch: raw sum {netSum:0.00} across {rowIndexes.Count} exact raw line(s) vs classified {a.Net:0.00}";

        // Whether the stated Gross is one invoice total repeated on every line
        // (RepeatedInvoiceTotal - compare the FIRST exact row's own Gross) or a true
        // per-line sum (PerLineAmount - compare the SUM of exactly these rows' own Gross)
        // is the same council-level fact AuditEngine.Run used when it built this row - not
        // re-guessed here.
        decimal rawGrossStated = repeatedInvoiceTotal ? firstGross : grossSum;
        if (Math.Abs(rawGrossStated - a.Gross) > 0.01m)
            return $"gross mismatch: raw {rawGrossStated:0.00} vs classified {a.Gross:0.00}";

        var expectedDescTrim = (a.Description ?? "").Trim();
        if (firstDesc is not null && !firstDesc.Equals(expectedDescTrim, StringComparison.Ordinal))
            return $"description mismatch: raw \"{firstDesc}\" vs classified \"{expectedDescTrim}\"";

        return null; // fully confirmed: exists at exactly these positions, Net/Gross/Description all match independently re-parsed raw text.
    }

    /// <summary>
    /// Re-derives one Schedule B member's Net/Gross/Description from exactly the raw row
    /// positions (<see cref="ScheduleBGroup.MemberRowIndexes"/>'s entry for this member -
    /// every raw line of that one (SourceTag, TransactionId) transaction, which can be more
    /// than one) and compares them to what the classified member claims. Gross is always
    /// compared against the FIRST exact row's own value, never a sum - Schedule B's member
    /// Gross is always the aggregate's first-published-line value (see
    /// <see cref="AuditEngine.Run"/>'s transAgg), regardless of the council's GrossMeaning.
    /// </summary>
    public static string? ConfirmedScheduleBMember(
        IReadOnlyList<string[]> table, ColumnMapping map, SpendRow member, IReadOnlyList<int> rowIndexes)
    {
        if (table.Count == 0) return "raw table is empty";
        if (rowIndexes.Count == 0)
            return "no member row indexes recorded for this transaction";

        var header = table[0].Select(h => h.Trim().TrimStart('﻿')).ToArray();
        int Idx(string? name) => name is null ? -1 : Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int iTrans = Idx(map.TransactionId), iSupplier = Idx(map.Supplier), iNet = Idx(map.Net),
            iGross = Idx(map.Gross), iVatAmt = Idx(map.VatAmount), iDesc = Idx(map.Description);

        decimal netSum = 0m, firstGross = 0m;
        string? firstDesc = null;
        for (int k = 0; k < rowIndexes.Count; k++)
        {
            int rowIdx = rowIndexes[k];
            int rawRowNum = rowIdx + 1;
            if (rawRowNum < 1 || rawRowNum >= table.Count)
                return $"member row index {rowIdx} out of range for raw table of {table.Count - 1} data rows";
            var row = table[rawRowNum];
            if (iTrans >= 0 && iTrans < row.Length && row[iTrans].Trim() != member.TransactionId)
                return $"transaction id mismatch at raw row {rawRowNum}: raw \"{row[iTrans].Trim()}\" vs classified \"{member.TransactionId}\"";
            if (iSupplier >= 0 && iSupplier < row.Length && row[iSupplier].Trim() != member.Supplier)
                return $"supplier mismatch at raw row {rawRowNum}: raw \"{row[iSupplier].Trim()}\" vs classified \"{member.Supplier}\"";

            decimal rowGross = iGross >= 0 && iGross < row.Length ? AuditEngine.ParseMoney(row[iGross]) : 0m;
            decimal rowNet = iNet >= 0 && iNet < row.Length ? AuditEngine.ParseMoney(row[iNet])
                : (iVatAmt >= 0 && iVatAmt < row.Length ? rowGross - AuditEngine.ParseMoney(row[iVatAmt]) : rowGross);
            netSum += rowNet;
            if (k == 0) { firstGross = rowGross; firstDesc = iDesc >= 0 && iDesc < row.Length ? row[iDesc].Trim() : null; }
        }

        if (Math.Abs(netSum - member.Net) > 0.01m)
            return $"net mismatch: raw sum {netSum:0.00} across {rowIndexes.Count} exact raw line(s) vs classified {member.Net:0.00}";

        if (Math.Abs(firstGross - member.Gross) > 0.01m)
            return $"gross mismatch: raw {firstGross:0.00} vs classified {member.Gross:0.00}";

        var expectedDescTrim = (member.Description ?? "").Trim();
        if (firstDesc is not null && !firstDesc.Equals(expectedDescTrim, StringComparison.Ordinal))
            return $"description mismatch: raw \"{firstDesc}\" vs classified \"{expectedDescTrim}\"";

        return null;
    }
}
