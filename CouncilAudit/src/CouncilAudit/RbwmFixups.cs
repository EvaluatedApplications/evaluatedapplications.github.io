namespace CouncilAudit;

/// <summary>
/// Royal Borough of Windsor and Maidenhead (council #6, onboarded 2026-10-03). Its "Payment to Suppliers" CSVs
/// (threshold: charges to a specific cost centre of GBP 100 or more) need two structural fixups, both confirmed by
/// hand against the 17 real files (2023-24 and 2024-25 annual, then monthly April 2025 to June 2026):
/// (1) 0-2 decorative rows (a title line "Supplier Payments where charge to specific cost centre is >= 100 for
/// June 2025" and blank rows of commas) precede the real header, which always starts "Organisation Name";
/// (2) two months (2025-08, 2025-09) were published with a different set of header NAMES over the SAME column
/// positions (Directorate(T), Sercop, Sercop(T), Supplier, Local supplier(internal ID), Purpose of spend(T)).
/// Each is a named, hand-confirmed rename, not a fuzzy match. Data rows also carry more cells than the header has
/// names (procurement-classification code and name pairs under blank headers); those are kept by
/// <see cref="AuditEngine.MapRows"/> OtherColumns as "ColN", so nothing the council published is dropped.
/// </summary>
public static class RbwmFixups
{
    public static List<string[]> SkipToHeader(List<string[]> table)
    {
        int start = table.FindIndex(r => r.Length > 0 && r[0].Trim().Equals("Organisation Name", StringComparison.OrdinalIgnoreCase));
        return start <= 0 ? table : table.Skip(start).ToList();
    }

    /// <summary>The council's own renames between publications, mapped back to the older (majority) names.</summary>
    public static readonly (string from, string to)[] HeaderFixups =
    {
        ("Directorate(T)", "Directorate"),
        ("Sercop", "Service Category Label"),
        ("Sercop(T)", "Service"),
        ("Supplier", "Supplier (Beneficiary name)"),
        ("Local supplier(internal ID)", "Local Supplier(beneficiary) internal reference"),
        ("Purpose of spend(T)", "Purpose of spend"),
    };

    /// <summary>
    /// Third structural fact, measured per file: in 2025-04, 2025-05 and 2025-06 the header names an "Irrecoverable
    /// VAT" column but the DATA rows do not carry that cell, so every later cell sits one place left of its header
    /// (the cell under "Purpose of spend" is then the procurement-classification CODE, e.g. 270000, and the real
    /// purpose text is under "Irrecoverable VAT"). Every other file has either "Not applicable"-style text (2023-24,
    /// 2024-25) or an empty cell (2025-07 onward) there. Detected from the data, not from the file name: if fewer
    /// than half the data rows hold an empty or "Not ..." value at that header position, an empty cell is inserted
    /// into every data row at that position, which realigns them. Found because the first run's Description for
    /// White Lodge Centre read "320000".
    /// </summary>
    public static List<string[]> AlignIrrecoverableVat(List<string[]> table)
    {
        if (table.Count < 2) return table;
        int idx = Array.FindIndex(table[0], h => h.Trim().Equals("Irrecoverable VAT", StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return table;
        int ok = 0, n = 0;
        for (int r = 1; r < table.Count; r++)
        {
            if (table[r].Length <= idx) continue;
            n++;
            var v = table[r][idx].Trim();
            if (v.Length == 0 || v.StartsWith("Not ", StringComparison.OrdinalIgnoreCase)) ok++;
        }
        if (n == 0 || ok * 2 >= n) return table;
        var result = new List<string[]>(table.Count) { table[0] };
        for (int r = 1; r < table.Count; r++)
        {
            var row = table[r].ToList();
            if (row.Count >= idx) row.Insert(idx, "");
            result.Add(row.ToArray());
        }
        return result;
    }

    /// <summary>All structural fixups in order: skip the preamble, rename drifted headers, realign shifted rows.</summary>
    public static List<string[]> Prepare(List<string[]> table)
    {
        var t = SkipToHeader(table);
        ApplyHeaderFixups(t);
        return AlignIrrecoverableVat(t);
    }

    /// <summary>The key used to recognise a payment line the council has already published in an EARLIER file.</summary>
    /// Session 52: a row the council published with no number now carries a per-row placeholder "(no number) N" (see
    /// <see cref="AuditEngine.MapRows(IReadOnlyList{string[]}, ColumnMapping, string, List{string}, List{DroppedRow}?)"/>), which differs
    /// between two files for the same payment, so the placeholder is left out of the key (the same rule as FileDuplication.IdForRepeat).
    public static (string, string, decimal, string?, string?) LineKey(SpendRow r) =>
        (FileDuplication.IdForRepeat(r.TransactionId), r.Supplier, r.Net, r.PayDateRaw, r.Description);

    /// <summary>
    /// Onboarding checklist step 6, measured on the real files: the council's monthly files are not disjoint.
    /// "2025-06" (named June 2025) holds 1,067 rows paid in June 2024, of which 1,002 are line-for-line the same
    /// payments as the 2024-25 annual file, and only about 100 rows paid in June 2025; "2025-04"/"2025-05" overlap
    /// by 381 keys, "2025-11"/"2025-12" by 61. Loading them all would double-count those payments in every schedule.
    /// Rows whose <see cref="LineKey"/> was already seen in an earlier file are dropped; repeated identical lines
    /// INSIDE one file are kept (those are genuine repeats for Schedule B to judge). Returns the kept rows and the
    /// number dropped; the caller adds this file's own keys to <paramref name="seenInEarlierFiles"/> afterwards.
    /// </summary>
    public static (List<SpendRow> kept, int dropped) DropAlreadyPublished(
        IReadOnlyList<SpendRow> rows, HashSet<(string, string, decimal, string?, string?)> seenInEarlierFiles)
    {
        var kept = new List<SpendRow>(rows.Count);
        int dropped = 0;
        foreach (var r in rows)
        {
            if (seenInEarlierFiles.Contains(LineKey(r))) dropped++;
            else kept.Add(r);
        }
        foreach (var r in rows) seenInEarlierFiles.Add(LineKey(r));
        return (kept, dropped);
    }

    public static void ApplyHeaderFixups(IReadOnlyList<string[]> table)
    {
        if (table.Count == 0) return;
        var h = table[0];
        for (int i = 0; i < h.Length; i++)
            foreach (var (from, to) in HeaderFixups)
                if (h[i].Trim().Equals(from, StringComparison.OrdinalIgnoreCase)) { h[i] = to; break; }
    }
}
