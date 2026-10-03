// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\AuditEngine.cs, copied 2026-10-03.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit the source repo from here;
// future engine changes happen upstream and get re-vendored into this copy by the Showroom owner.

using System.Globalization;

namespace CouncilAudit;

/// <summary>
/// Turns raw rows (already mapped onto <see cref="ColumnMapping"/>) into the three
/// schedules proven on Wokingham: A (amount mismatch per transaction+supplier),
/// B (repeated payments under different transaction numbers), D (one transaction
/// number covering more than one payee or pay date). No threshold, no "likely
/// innocent" filtering, no accusation - every rule here reports a fact about the
/// published numbers, nothing about intent. See PRODUCT_PLAN.md for why.
/// </summary>
public static class AuditEngine
{
    public static List<SpendRow> MapRows(
        IReadOnlyList<string[]> table, ColumnMapping map, string sourceTag, List<string> warnings)
    {
        if (table.Count == 0) return new();
        var header = table[0].Select(h => h.Trim().TrimStart('﻿')).ToArray();

        int Idx(string? name) => name is null ? -1
            : Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

        int iTrans = Idx(map.TransactionId);
        int iSupplier = Idx(map.Supplier);
        int iPayDate = Idx(map.PayDate);
        int iDesc = Idx(map.Description);
        int iService = Idx(map.ServiceArea);
        int iNet = Idx(map.Net);
        int iGross = Idx(map.Gross);
        int iVatAmt = Idx(map.VatAmount);
        int iVatType = Idx(map.VatType);

        if (iTrans < 0) warnings.Add($"{sourceTag}: transaction-id column \"{map.TransactionId}\" not found in header.");
        if (iSupplier < 0) warnings.Add($"{sourceTag}: supplier column \"{map.Supplier}\" not found in header.");
        if (iGross < 0) warnings.Add($"{sourceTag}: gross/amount column \"{map.Gross}\" not found in header.");

        var rows = new List<SpendRow>();
        for (int r = 1; r < table.Count; r++)
        {
            var f = table[r];
            string Get(int idx) => idx >= 0 && idx < f.Length ? f[idx] : "";

            if (iTrans >= 0 && string.IsNullOrWhiteSpace(Get(iTrans))) continue;

            decimal gross = ParseMoney(Get(iGross));
            decimal? vatAmount = iVatAmt >= 0 ? ParseMoney(Get(iVatAmt)) : null;
            string? vatType = iVatType >= 0 ? Get(iVatType).Trim() : null;

            decimal net;
            if (iNet >= 0)
            {
                net = ParseMoney(Get(iNet));
            }
            else if (vatAmount.HasValue)
            {
                // Council publishes Gross + VAT directly (e.g. Merton) rather than Net + a
                // VAT-type code (e.g. Wokingham). Net is derived, not published - flagged in
                // ScheduleARow.LineCount context via Warnings once per source, not per row.
                net = gross - vatAmount.Value;
            }
            else
            {
                net = gross; // no VAT information published at all; expect net == gross.
            }

            var payDateRaw = iPayDate >= 0 ? Get(iPayDate) : null;
            var payDateParsed = ParseDate(payDateRaw);

            rows.Add(new SpendRow(
                sourceTag,
                Get(iTrans).Trim(),
                Get(iSupplier).Trim(),
                payDateRaw,
                payDateParsed,
                iDesc >= 0 ? Get(iDesc) : null,
                iService >= 0 ? Get(iService) : null,
                net,
                gross,
                vatAmount,
                vatType,
                r - 1));
        }
        return rows;
    }

    /// <summary>
    /// Several councils redact the supplier name and/or transaction id on rows that
    /// would otherwise identify a vulnerable person (children's and adult social care
    /// placements, safeguarding). Confirmed on real Merton data: 9,942 of ~45k rows in
    /// one year carry "REDACTED" in place of a real identifier, and the same redacted
    /// placeholder text is shared across many unrelated payments (e.g. "RED11538ACTED"
    /// covering 19 different, unrelated amounts). Grouping those rows as if the
    /// placeholder were a real transaction or supplier id produces nonsense mismatches
    /// (Merton's first pass: 36,967 false "Schedule A" rows, traced to exactly this).
    /// These rows are excluded from every schedule, not flagged as a discrepancy - a
    /// redaction is the council withholding identifying data lawfully, not a published
    /// number that fails to reconcile.
    /// </summary>
    private static bool IsRedactedPlaceholder(string s) =>
        s.Contains("REDACT", StringComparison.OrdinalIgnoreCase);

    public static AuditResult Run(
        IReadOnlyList<SpendRow> all,
        TransactionIdScope idScope = TransactionIdScope.CouncilWideUnique,
        GrossMeaning grossMeaning = GrossMeaning.RepeatedInvoiceTotal,
        List<string>? warnings = null)
    {
        warnings ??= new List<string>();

        var redactedCount = all.Count(r => IsRedactedPlaceholder(r.TransactionId) || IsRedactedPlaceholder(r.Supplier));
        if (redactedCount > 0)
        {
            warnings.Add(
                $"{redactedCount} rows carry a redacted transaction id or supplier name (safeguarding/personal-data " +
                "withholding) and are excluded from every schedule below: the placeholder is shared across unrelated " +
                "payments, so grouping by it produces false matches, not real discrepancies.");
            all = all.Where(r => !IsRedactedPlaceholder(r.TransactionId) && !IsRedactedPlaceholder(r.Supplier)).ToList();
        }

        // ---- Schedule A: amount mismatch, grouped by (SourceTag, TransactionId, Supplier) ----
        // A transaction number is not always one invoice to one payee (Schedule D records
        // that fact separately); grouping by transaction+supplier avoids mixing two payees'
        // lines into one sum. Gross is the one invoice amount repeated on every line of the
        // transaction; comparing a per-supplier net sum against it is itself the discrepancy
        // this schedule records when a transaction has more than one supplier.
        var bySupplierGroups = all.GroupBy(r => (r.SourceTag, r.TransactionId, r.Supplier)).ToList();
        var scheduleA = new List<ScheduleARow>();
        foreach (var g in bySupplierGroups)
        {
            var members = g.ToList();
            var first = members[0];
            decimal netSum = members.Sum(r => r.Net);
            // What "the stated amount" is for a multi-line group depends on what Gross
            // means for this council (see GrossMeaning) - never on whether the values
            // happen to repeat. Equal instalments and a repeated invoice total are
            // indistinguishable by pattern alone.
            decimal grossStated = grossMeaning == GrossMeaning.RepeatedInvoiceTotal
                ? first.Gross
                : members.Sum(r => r.Gross);
            // Net-vs-gross reconciliation: either the council published a VAT amount
            // directly per row (Merton: net was already derived as Gross-VatAmount, so
            // net+vat reproduces gross by construction and must not be re-uplifted by a
            // VAT-type code), or it published a VAT-type code instead (Wokingham), or
            // neither (expect gross == net).
            decimal? vatAmountSum = members.Any(r => r.VatAmount.HasValue)
                ? members.Sum(r => r.VatAmount ?? 0m) : null;
            decimal expected = vatAmountSum.HasValue
                ? Math.Round(netSum + vatAmountSum.Value, 2, MidpointRounding.AwayFromZero)
                : VatAdjustedExpectedGross(netSum, first.VatType);
            decimal diff = grossStated - expected;
            if (Math.Abs(diff) > 0.01m)
            {
                scheduleA.Add(new ScheduleARow(
                    first.SourceTag, first.TransactionId, first.Supplier, first.PayDateRaw,
                    netSum, grossStated, expected, grossStated - netSum,
                    first.VatType, first.ServiceArea, first.Description, members.Count));
            }
        }

        // ---- Schedule B: repeated payments across different transaction numbers ----
        // Same supplier + net + gross + description + pay date, different transaction id.
        // Blank/unpublished pay dates are excluded from this grouping only (two unpublished
        // dates cannot be asserted equal to each other - that is a missing-data fact about
        // the source, not a judgement call about which matches are "real").
        var transAgg = all.GroupBy(r => (r.SourceTag, r.TransactionId))
            .Select(g =>
            {
                var members = g.ToList();
                var first = members[0];
                return first with { Net = members.Sum(r => r.Net) };
            })
            .ToList();

        var withPayDate = transAgg.Where(r => !string.IsNullOrWhiteSpace(r.PayDateRaw)).ToList();
        int blankPayDateCount = transAgg.Count - withPayDate.Count;
        if (blankPayDateCount > 0)
            warnings.Add($"{blankPayDateCount} transactions have no Pay Date published; excluded from Schedule B's same-pay-date grouping only.");

        var dupGroups = withPayDate
            .GroupBy(r => (r.Supplier, r.Net, r.Gross, (r.Description ?? "").Trim(), r.PayDateRaw))
            .Where(g => g.Select(r => (r.SourceTag, r.TransactionId)).Distinct().Count() > 1)
            .ToList();

        var scheduleB = new List<ScheduleBGroup>();
        int groupId = 0;
        foreach (var g in dupGroups)
        {
            groupId++;
            var members = g.OrderBy(r => r.SourceTag).ThenBy(r => r.TransactionId, StringComparer.Ordinal).ToList();
            scheduleB.Add(new ScheduleBGroup(groupId, g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Key.Item4, g.Key.Item5, members));
        }

        // ---- Schedule D: one transaction number, more than one payee or pay date ----
        // Only a meaningful finding when TransactionId is council-wide unique (the
        // council itself assigns and tracks it, e.g. Wokingham's TransNo). When it is
        // supplier-furnished (the council just republishes the supplier's own invoice
        // number, e.g. Merton's "Supplier Invoice No"), two unrelated suppliers using
        // the same number is coincidence, not a multi-payee transaction - confirmed on
        // real Merton data (trans 30970: BIRKIN CLEANING and MORE HOUSE SCHOOL, two
        // unrelated invoices, same number by chance). Still computed either way (the
        // rows genuinely exist as published) but tagged with the scope so no caller
        // reports it as a discrepancy under the wrong scope.
        var byTransRaw = all.GroupBy(r => (r.SourceTag, r.TransactionId)).ToList();
        var scheduleD = byTransRaw
            .Where(g => g.Select(r => r.Supplier).Distinct().Count() > 1
                     || g.Select(r => r.PayDateRaw).Distinct().Count() > 1)
            .Select(g => new ScheduleDGroup(
                g.Key.SourceTag, g.Key.TransactionId,
                g.Select(r => r.Supplier).Distinct().Count(),
                g.Select(r => r.PayDateRaw).Distinct().Count(),
                g.OrderBy(r => r.PayDateRaw).ToList()))
            .ToList();
        if (idScope == TransactionIdScope.SupplierFurnished && scheduleD.Count > 0)
            warnings.Add(
                $"Schedule D found {scheduleD.Count} transaction numbers shared by more than one supplier, but this " +
                "council's transaction id is supplier-furnished (its own invoice number), not council-wide unique, " +
                "so these are most likely coincidental number reuse between unrelated suppliers, not evidence of a " +
                "shared transaction. Do not present Schedule D as a finding for this council.");

        return new AuditResult(
            all.Count, bySupplierGroups.Count, scheduleA, scheduleB, scheduleD, idScope, warnings);
    }

    /// <summary>
    /// The VAT uplift a published VAT-TYPE code itself asserts, and nothing else. Only
    /// applied when the net figure was published directly (VatType field in use); when
    /// net was derived from Gross-VatAmount this is not needed (the subtraction already
    /// used the real, per-row VAT amount).
    /// EXEM/NBUS/OSCP/ZERO: no uplift. RRTE: 5%. STD: 20%. Anything else/blank: expect
    /// gross == net (the only honest default with no VAT information at all).
    /// </summary>
    private static decimal VatAdjustedExpectedGross(decimal net, string? vatTypeRaw)
    {
        if (string.IsNullOrWhiteSpace(vatTypeRaw))
            return Math.Round(net, 2, MidpointRounding.AwayFromZero);
        var v = vatTypeRaw.Trim().ToUpperInvariant();
        decimal mult =
            v.StartsWith("EXEM") || v.StartsWith("NBUS") || v.StartsWith("OSCP") || v.StartsWith("ZERO") ? 1.00m :
            v.StartsWith("RRTE") ? 1.05m :
            v.StartsWith("STD") ? 1.20m :
            1.00m;
        return Math.Round(net * mult, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ParseMoney(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        s = s.Replace("£", "").Replace(",", "").Trim();
        if (s.StartsWith("(") && s.EndsWith(")")) s = "-" + s.Trim('(', ')');
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    public static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        // Excel serial date (xlsx numeric cell, or a CSV that kept the serial as text).
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)
            && !s.Contains('/') && !s.Contains('-') && serial > 20000 && serial < 60000)
        {
            try { return DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(serial)); }
            catch { return null; }
        }
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "dd-MMM-yy", "d-MMM-yy", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return DateOnly.FromDateTime(dt2);
        return null;
    }
}
