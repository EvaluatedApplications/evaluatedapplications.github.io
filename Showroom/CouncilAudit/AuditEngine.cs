// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\AuditEngine.cs, re-synced 2026-10-03 (Session 25/26 sync): Session 26: multi-payee Schedule A rows, mixed-VAT-label expectation, standing-payment classes (StandingSchedule), lossy scientific-notation ids ("~row<n>"), OtherColumns.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

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
    /// <summary>Session 23: every non-empty cell whose column is not mapped, "Header=value|..." (a blank header
    /// is written "ColN", N = 1-based position).
    /// Null when there is none. Keeps the engine from ever silently dropping a published column.</summary>
    public static string? OtherColumnsText(string[] header, string[] f, HashSet<int> mapped)
    {
        System.Text.StringBuilder? sb = null;
        for (int i = 0; i < f.Length; i++) // iterate the DATA row: cells beyond the header's width are published too
        {
            if (mapped.Contains(i)) continue;
            var v = f[i]?.Trim();
            if (string.IsNullOrEmpty(v)) continue;
            // a cell under a blank (or missing) header (RBWM's classification pairs, Merton's row index) is still published data
            string name = i < header.Length && !string.IsNullOrWhiteSpace(header[i]) ? header[i] : $"Col{i + 1}";
            (sb ??= new()).Append(sb.Length > 0 ? "|" : "").Append(name).Append('=').Append(v);
        }
        return sb?.ToString();
    }

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
        int iInvoiceNo = Idx(map.SupplierInvoiceNumber);
        int iCostCentre = Idx(map.CostCentreArea);
        int iInvoiceType = Idx(map.InvoiceType);

        if (iTrans < 0) warnings.Add($"{sourceTag}: transaction-id column \"{map.TransactionId}\" not found in header.");
        if (iSupplier < 0) warnings.Add($"{sourceTag}: supplier column \"{map.Supplier}\" not found in header.");
        if (iGross < 0) warnings.Add($"{sourceTag}: gross/amount column \"{map.Gross}\" not found in header.");

        var mapped = new HashSet<int>(new[] { iTrans, iSupplier, iPayDate, iDesc, iService, iNet, iGross,
            iVatAmt, iVatType, iInvoiceNo, iCostCentre, iInvoiceType }.Where(i => i >= 0));

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
                r - 1,
                iInvoiceNo >= 0 ? Get(iInvoiceNo).Trim() : null,
                iCostCentre >= 0 ? Get(iCostCentre).Trim() : null,
                iInvoiceType >= 0 ? Get(iInvoiceType).Trim() : null,
                OtherColumnsText(header, f, mapped)));
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
    /// <summary>
    /// Fix (this session): the plain "contains REDACT" check this replaces does NOT catch
    /// the real Merton numeric-infix format quoted in the comment above it
    /// ("RED11538ACTED" - a digit run spliced between "RED" and "ACTED", presumably so the
    /// placeholder still sorts/displays as distinct text per row) - "RED11538ACTED" has no
    /// consecutive "REDACT" substring at all, so every row using that exact format was
    /// silently NOT excluded, contrary to what the comment claimed. Caught by writing the
    /// regression test for this council's own documented example, not by re-reading the
    /// prose. Now matches both the plain "REDACTED..." form and the split RED...ACTED form.
    /// </summary>
    private static bool IsRedactedPlaceholder(string s) =>
        s.Contains("REDACT", StringComparison.OrdinalIgnoreCase)
        || (s.StartsWith("RED", StringComparison.OrdinalIgnoreCase)
            && s.EndsWith("ACTED", StringComparison.OrdinalIgnoreCase)
            && s.Length >= 8);

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

        // Session 26: a transaction id that a spreadsheet has turned into scientific notation ("2.02408E+11") has lost
        // its digits and is SHARED by unrelated payments (Merton: 334 rows, 66 such strings, up to 59 rows on one).
        // Grouping by it would sum unrelated lines into one "transaction", so each such row becomes its own
        // transaction, tagged with its source row so the id stays traceable.
        int lossyIds = all.Count(r => IsLossyTransactionId(r.TransactionId));
        if (lossyIds > 0)
        {
            warnings.Add($"{lossyIds} rows carry a transaction id that a spreadsheet turned into scientific notation " +
                         "(digits lost, shared by unrelated payments); each is treated as its own transaction, id suffixed \"~row<n>\".");
            all = all.Select(r => IsLossyTransactionId(r.TransactionId)
                ? r with { TransactionId = r.TransactionId + "~row" + r.RowIndexInSource } : r).ToList();
        }

        // ---- Schedule A: amount mismatch, grouped by (SourceTag, TransactionId, Supplier) ----
        // A transaction number is not always one invoice to one payee (Schedule D records
        // that fact separately); grouping by transaction+supplier avoids mixing two payees'
        // lines into one sum. Gross is the one invoice amount repeated on every line of the
        // transaction; comparing a per-supplier net sum against it is itself the discrepancy
        // this schedule records when a transaction has more than one supplier.
        var bySupplierGroups = all.GroupBy(r => (r.SourceTag, r.TransactionId, r.Supplier)).ToList();
        var scheduleA = new List<ScheduleARow>();

        // Session 26: MULTI-PAYEE TRANSACTIONS are tested once, at transaction level. When a transaction number
        // spans three or more suppliers and one Gross is repeated on every line, that Gross is the transaction's
        // figure, so comparing it with each supplier's own sum (what the per-supplier pass below does) can only
        // fail, once per supplier. Wokingham: 10 such transactions (academy payment runs, up to 26 suppliers and
        // 134 lines each) produced 230 per-supplier "Unreconciled" rows; none reconcile as a whole either, so each
        // is reported as ONE transaction row, and the per-supplier rows are not.
        var multiPayee = new HashSet<(string, string)>();
        if (grossMeaning == GrossMeaning.RepeatedInvoiceTotal)
        {
            foreach (var t in all.GroupBy(r => (r.SourceTag, r.TransactionId)))
            {
                if (t.Select(r => r.Supplier).Distinct().Count() < 3) continue;
                if (t.Select(r => r.Gross).Distinct().Count() != 1) continue;
                multiPayee.Add((t.Key.SourceTag, t.Key.TransactionId));
                var tm = t.ToList();
                var tIn = ComputeScheduleAGroupInputs(tm, grossMeaning);
                if (tIn is null) continue; // reconciles as a whole transaction: nothing to report.
                var (tNet, tGross, tExp, tDiff) = tIn.Value;
                int nSup = tm.Select(r => r.Supplier).Distinct().Count();
                scheduleA.Add(new ScheduleARow(
                    tm[0].SourceTag, tm[0].TransactionId, $"{tm[0].Supplier} (+{nSup - 1} other payees)", tm[0].PayDateRaw,
                    tNet, tGross, tExp, tGross - tNet, tm[0].VatType, tm[0].ServiceArea, tm[0].Description, tm.Count,
                    ScheduleAClassification.Unreconciled,
                    $"multi-payee transaction: {tm.Count} lines to {nSup} suppliers sum to {tNet:0.00} (VAT-uplifted {tExp:0.00}), " +
                    $"but the one Gross repeated on every line is {tGross:0.00}. Tested once as a whole transaction because a " +
                    "per-supplier comparison against a transaction-wide figure cannot reconcile.",
                    tm.Select(m => m.RowIndexInSource).ToList()));
            }
        }

        foreach (var g in bySupplierGroups)
        {
            if (multiPayee.Contains((g.Key.SourceTag, g.Key.TransactionId))) continue;
            var members = g.ToList();
            var first = members[0];
            var inputs = ComputeScheduleAGroupInputs(members, grossMeaning);
            if (inputs is null)
                continue; // reconciles under one of the two comparisons - nothing to report.
            var (netSum, grossStated, expected, diff) = inputs.Value;

            // ---- Classify before reporting, so the schedule doesn't drown real exceptions ----
            // Session 20: the cascade itself now lives in ClassifyScheduleA (extract-method
            // only, zero behaviour change - see BASELINE.md/FEEDBACK.md for the EvalApp 3.0.0
            // Classify port this extraction exists to make possible, benchmarked separately
            // in scratch/evalapp-classify-bench, NOT wired in here).
            var (classification, detail) = ClassifyScheduleA(members, netSum, grossStated, diff, grossMeaning, first.VatType, expected);

            scheduleA.Add(new ScheduleARow(
                first.SourceTag, first.TransactionId, first.Supplier, first.PayDateRaw,
                netSum, grossStated, expected, grossStated - netSum,
                first.VatType, first.ServiceArea, first.Description, members.Count,
                classification, detail,
                members.Select(m => m.RowIndexInSource).ToList()));
        }

        // Rank so what the rules can't explain rises to the top: Unreconciled first (the
        // actual headline target - ask the council), then DebtCharge and DoubleListing
        // (recognised, labelled, not hidden, but not where attention should go first),
        // each ordered by the size of the gap.
        scheduleA = scheduleA
            .OrderBy(a => a.Classification == ScheduleAClassification.Unreconciled ? 0 : 1)
            .ThenByDescending(a => Math.Abs(a.Difference))
            .ToList();
        // ---- Schedule B: repeated payments across different transaction numbers ----
        // Same supplier + net + gross + description + pay date, different transaction id.
        // Blank/unpublished pay dates are excluded from this grouping only (two unpublished
        // dates cannot be asserted equal to each other - that is a missing-data fact about
        // the source, not a judgement call about which matches are "real").
        // Session 19: carry the full set of raw row positions for EVERY line of each
        // (SourceTag, TransactionId) transaction alongside the aggregate, not just the
        // first line's own RowIndexInSource - so a later hand-check can re-sum exactly
        // these rows by position instead of re-discovering them with a text scan (see
        // HandCheckHelpers.cs / Models.cs's ScheduleBGroup.MemberRowIndexes).
        var transAgg = all.GroupBy(r => (r.SourceTag, r.TransactionId))
            .Select(g =>
            {
                var members = g.ToList();
                var first = members[0];
                return (
                    Rep: first with { Net = members.Sum(r => r.Net) },
                    RowIndexes: (IReadOnlyList<int>)members.Select(r => r.RowIndexInSource).ToList());
            })
            .ToList();

        var withPayDate = transAgg.Where(x => !string.IsNullOrWhiteSpace(x.Rep.PayDateRaw)).ToList();
        int blankPayDateCount = transAgg.Count - withPayDate.Count;
        if (blankPayDateCount > 0)
            warnings.Add($"{blankPayDateCount} transactions have no Pay Date published; excluded from Schedule B's same-pay-date grouping only.");

        var dupGroups = withPayDate
            .GroupBy(x => (x.Rep.Supplier, x.Rep.Net, x.Rep.Gross, (x.Rep.Description ?? "").Trim(), x.Rep.PayDateRaw))
            .Where(g => g.Select(x => (x.Rep.SourceTag, x.Rep.TransactionId)).Distinct().Count() > 1)
            .ToList();

        var scheduleB = new List<ScheduleBGroup>();
        int groupId = 0;
        // Built once, and only if a group actually needs it.
        var standingIndex = new Lazy<Dictionary<(string Supplier, decimal Net), StandingSchedule.Series>>(() =>
            StandingSchedule.Build(transAgg.Select(x => x.Rep), r => IsCreditLikeInvoiceType(r.InvoiceType) || r.Net < 0m));
        foreach (var g in dupGroups)
        {
            groupId++;
            var ordered = g.OrderBy(x => x.Rep.SourceTag).ThenBy(x => x.Rep.TransactionId, StringComparer.Ordinal).ToList();
            var members = ordered.Select(x => x.Rep).ToList();
            var memberRowIndexes = ordered.Select(x => x.RowIndexes).ToList();

            // Same supplier invoice number repeated = likely duplicate; different invoice
            // numbers at what is otherwise an identical amount = a standardised recurring
            // rate (checklist 4b, confirmed on real Reading data: Sean Heath's group repeats
            // one amount under TWO DIFFERENT invoice numbers - a legitimate recurring rent
            // payment; Freeborn's and Lynx Lettings's groups repeat the SAME invoice number
            // twice each - a genuine duplicate-payment candidate). Redacted rows never reach
            // here (excluded above), so a redacted payee is never compared this way.
            var invoiceNumbers = members.Select(m => m.SupplierInvoiceNumber).ToList();
            var classification = ScheduleBClassification.Unclear;
            string? detail = null;
            if (invoiceNumbers.All(n => !string.IsNullOrWhiteSpace(n)))
            {
                var distinct = invoiceNumbers.Distinct().ToList();
                if (distinct.Count == 1)
                {
                    classification = ScheduleBClassification.LikelyDuplicate;
                    detail = $"all {members.Count} members share supplier invoice number \"{distinct[0]}\".";
                }
                else
                {
                    classification = ScheduleBClassification.LikelyRecurring;
                    detail = $"members carry {distinct.Count} different supplier invoice numbers " +
                              $"({string.Join(", ", distinct.Take(5))}) despite the identical amount - " +
                              "consistent with a standardised recurring rate, not a repeat payment.";
                }
            }

            // Session 26: no invoice number to settle it, so ask whether this (supplier, amount) is a STANDING
            // monthly payment whose count over time explains the repeat (see StandingSchedule).
            if (classification == ScheduleBClassification.Unclear && !members.Any(m => IsCreditLikeInvoiceType(m.InvoiceType) || m.Net < 0m))
            {
                var gd = members[0].PayDate ?? ParseDate(members[0].PayDateRaw);
                if (gd is not null
                    && StandingSchedule.Classify(g.Key.Item1, g.Key.Item2, gd.Value, members.Count, standingIndex.Value) is { } st)
                {
                    classification = st.Classification;
                    detail = st.Detail;
                }
            }

            scheduleB.Add(new ScheduleBGroup(groupId, g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Key.Item4, g.Key.Item5,
                members, classification, detail, memberRowIndexes));
        }

        // Same ranking principle as Schedule A: groups the rules can't explain either way
        // (Unclear - no invoice number was available to check) rise above the ones already
        // labelled one way or the other, largest value first within each band.
        scheduleB = scheduleB
            .OrderBy(b => b.Classification is ScheduleBClassification.Unclear or ScheduleBClassification.StandingScheduleSurplus ? 0
                        : b.Classification == ScheduleBClassification.LikelyDuplicate ? 1 : 2)
            .ThenByDescending(b => b.Net * b.Members.Count)
            .ToList();

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

        // ---- Schedule R: credit/refund rows (council's own Invoice Type label, not an
        // inferred sign) and whether a same-supplier charge of matching magnitude exists
        // anywhere in the loaded data (Session 14, built for Reading - the only onboarded
        // council that publishes an Invoice Type column; structurally empty for any
        // council that doesn't map it, same "not a bug" principle as Reading's own empty
        // Schedule A). Deliberately generous (any matching-magnitude non-credit row for
        // the same supplier within a year, not a strict one-to-one reconciliation) - the
        // point is "does a plausible explanation exist in the data", the same question
        // Schedule A's VAT/debt-charge/double-listing checks answer for Wokingham's shape.
        var scheduleR = new List<ScheduleRRow>();
        var creditLike = all.Where(r => IsCreditLikeInvoiceType(r.InvoiceType)).ToList();
        if (creditLike.Count > 0)
        {
            var chargesBySupplier = all
                .Where(r => !IsCreditLikeInvoiceType(r.InvoiceType))
                .GroupBy(r => r.Supplier)
                .ToDictionary(g => g.Key, g => g.ToList());

            var creditStanding = new Lazy<Dictionary<(string Supplier, decimal Amount), string>>(
                () => StandingSchedule.BuildCreditSeries(creditLike));
            foreach (var c in creditLike)
            {
                var (match, splitMatchA, splitMatchB) = FindScheduleRMatches(c, chargesBySupplier);

                // Session 20: extract-method only (zero behaviour change) - see ClassifyScheduleR
                // below, the twin of ClassifyScheduleA, both ported to an EvalApp 3.0.0 Classify
                // cascade and benchmarked in scratch/evalapp-classify-bench, NOT wired in here.
                var (classification, detail) = ClassifyScheduleR(c, match, splitMatchA, splitMatchB);
                // Session 26: an unmatched "refund" whose amount recurs monthly under the refund label is a fixed
                // recurring transfer recorded with that label (Reading -> Brighter Futures for Children from 2024-03).
                if (classification == CreditMatchClassification.Unmatched && c.Gross > 0m
                    && creditStanding.Value.TryGetValue((c.Supplier, Math.Abs(c.Gross)), out var standingDetail))
                {
                    classification = CreditMatchClassification.StandingPaymentUnderRefundType;
                    detail = standingDetail;
                }
                scheduleR.Add(new ScheduleRRow(
                    c.SourceTag, c.TransactionId, c.Supplier, c.Gross, c.InvoiceType, c.PayDateRaw,
                    c.Description, classification, detail, c.RowIndexInSource));
            }

            scheduleR = scheduleR
                .OrderBy(r => r.Classification == CreditMatchClassification.Unmatched ? 0 : 1)
                .ThenByDescending(r => Math.Abs(r.Amount))
                .ToList();
        }

        return new AuditResult(
            all.Count, bySupplierGroups.Count, scheduleA, scheduleB, scheduleD, idScope, warnings, scheduleR);
    }

    /// <summary>
    /// Schedule R's match search (Session 20: extracted out of <see cref="Run"/>, zero
    /// behaviour change - lets the benchmark harness in scratch/evalapp-classify-bench
    /// build real (match, splitMatchA, splitMatchB) tuples from real data without
    /// re-deriving the search itself). See <see cref="ClassifyScheduleR"/> for what each
    /// outcome means.
    /// </summary>
    public static (SpendRow? Match, SpendRow? SplitMatchA, SpendRow? SplitMatchB) FindScheduleRMatches(
        SpendRow c, IReadOnlyDictionary<string, List<SpendRow>> chargesBySupplier)
    {
        SpendRow? match = null;
        if (chargesBySupplier.TryGetValue(c.Supplier, out var charges))
        {
            match = charges.FirstOrDefault(ch =>
                Math.Abs(Math.Abs(ch.Gross) - Math.Abs(c.Gross)) <= 0.01m
                && WithinDateWindow(c.PayDate, ch.PayDate, 365));
        }

        // Session 15 widening: a structural probe over the FULL Unmatched population
        // (not a judgement sample) found 125 credits whose magnitude equals the SUM of
        // two same-supplier charge lines in the window, but only 10 of those 125 share
        // the credit's own Voucher Number with BOTH charge lines - i.e. a genuine
        // same-voucher multi-line settlement (one voucher, one credit line plus two
        // charge lines that together net it out), the same phenomenon already named in
        // Reading's own KnownQuirks for Hexagon venue settlements, just split across two
        // lines instead of one. Hand-opened and confirmed real for theatre/promoter
        // suppliers (STRICTLY THEATRE CO, SJM LTD, KILIMANJARO LIVE, AEG PRESENTS,
        // DANIEL MARTINEZ FLAMENCO COMPANY, SIGNIS). The other 115/125 cross-voucher
        // "coincidental sum" splits are deliberately NOT coded here - a frequently-paid
        // supplier will turn up SOME pair of charges summing near any given credit by
        // chance within a year window, and that risk is exactly the "standardised
        // recurring rate" coincidence trap this project has already named for Schedule
        // B (ONBOARDING_CHECKLIST.md 4b); restricting to the credit's OWN voucher number
        // removes that coincidence risk entirely (the two charge lines are definitionally
        // part of the very same published transaction as the credit, not a different one
        // that merely adds up). Bounded to voucher groups of at most 50 lines, both to
        // keep the pairwise scan cheap and because a REED-sized (333-line) voucher is
        // exactly the shape where a same-voucher pair match would be most likely to be
        // coincidental rather than a genuine 3-line settlement.
        SpendRow? splitMatchA = null, splitMatchB = null;
        if (match is null && chargesBySupplier.TryGetValue(c.Supplier, out var sameSupplierCharges))
        {
            var sameVoucher = sameSupplierCharges
                .Where(ch => ch.SourceTag == c.SourceTag && ch.TransactionId == c.TransactionId)
                .ToList();
            if (sameVoucher.Count >= 2 && sameVoucher.Count <= 50)
            {
                decimal target = Math.Abs(c.Gross);
                for (int i = 0; i < sameVoucher.Count && splitMatchA is null; i++)
                    for (int j = i + 1; j < sameVoucher.Count && splitMatchA is null; j++)
                        if (Math.Abs(Math.Abs(sameVoucher[i].Gross) + Math.Abs(sameVoucher[j].Gross) - target) <= 0.01m)
                        {
                            splitMatchA = sameVoucher[i];
                            splitMatchB = sameVoucher[j];
                        }
            }
        }

        return (match, splitMatchA, splitMatchB);
    }

    /// <summary>
    /// A row is credit/refund-like if the council's own published Invoice Type says so
    /// - confirmed real on Reading: "CREDIT" (2021-2023 files, always negative amounts)
    /// and "RBC Refunds Manual Entry"/"RBC AR REFUNDS" (2024 onward, always POSITIVE -
    /// the council's own sign convention for a refund changed between publishing eras,
    /// which is exactly why this reads the label, not the sign of Amount).
    /// </summary>
    private static bool IsCreditLikeInvoiceType(string? invoiceType) =>
        !string.IsNullOrWhiteSpace(invoiceType)
        && (invoiceType.Contains("CREDIT", StringComparison.OrdinalIgnoreCase)
            || invoiceType.Contains("REFUND", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True if both dates are known and within <paramref name="days"/> of each other, OR
    /// either date is unpublished (lenient on missing data - this schedule's job is to
    /// surface a plausible explanation, not exclude one for a missing date; a future
    /// cycle could tighten this if missing dates turn out to hide real mismatches).
    /// </summary>
    private static bool WithinDateWindow(DateOnly? a, DateOnly? b, int days)
    {
        if (a is null || b is null) return true;
        return Math.Abs(a.Value.DayNumber - b.Value.DayNumber) <= days;
    }

    /// <summary>
    /// Schedule A's pre-classification arithmetic (Session 20: extracted out of
    /// <see cref="Run"/>, zero behaviour change - so the benchmark harness in
    /// scratch/evalapp-classify-bench can build real (netSum, grossStated, expected, diff)
    /// tuples from real data without re-deriving the VAT/reverse-charge arithmetic itself).
    /// Returns null when the group reconciles under either the VAT-uplifted or the raw-net
    /// comparison - i.e. there is nothing for <see cref="ClassifyScheduleA"/> to classify.
    /// </summary>
    public static (decimal NetSum, decimal GrossStated, decimal Expected, decimal Diff)? ComputeScheduleAGroupInputs(
        IReadOnlyList<SpendRow> members, GrossMeaning grossMeaning)
    {
        var first = members[0];
        decimal netSum = members.Sum(r => r.Net);
        decimal grossStated = grossMeaning == GrossMeaning.RepeatedInvoiceTotal
            ? first.Gross
            : members.Sum(r => r.Gross);
        decimal? vatAmountSum = members.Any(r => r.VatAmount.HasValue)
            ? members.Sum(r => r.VatAmount ?? 0m) : null;
        // Session 26: a group whose lines carry DIFFERENT VAT Type labels (e.g. EDF Energy: some
        // lines Standard, some Reduced) is uplifted line by line, each at its own label's rate;
        // using only the first line's label for the whole group mis-stated the expected gross.
        // Measured on Wokingham's Unreconciled set: 971 mixed-label groups, 724 reconcile to
        // the penny per line.
        decimal expected = vatAmountSum.HasValue
            ? Math.Round(netSum + vatAmountSum.Value, 2, MidpointRounding.AwayFromZero)
            : HasMixedVatLabels(members)
                ? ClosestTo(grossStated, PerLineExpectedGross(members, flipSign: false), VatAdjustedExpectedGross(netSum, first.VatType))
                : VatAdjustedExpectedGross(netSum, first.VatType);
        decimal diff = grossStated - expected;
        decimal rawDiff = grossStated - Math.Round(netSum, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(diff) > 0.01m && Math.Abs(rawDiff) <= 0.01m)
            return null; // reverse charge (or any no-uplift case): raw net already reconciles.
        if (Math.Abs(diff) <= 0.01m)
            return null; // VAT-uplifted comparison already reconciles.
        return (netSum, grossStated, expected, diff);
    }

    /// <summary>
    /// Schedule A's classification cascade (Session 20: extracted out of <see cref="Run"/>,
    /// zero behaviour change - exists so the exact same decision logic can be compared
    /// against an EvalApp 3.0.0 <c>Classify</c>-based port in
    /// scratch/evalapp-classify-bench, not wired into that pipeline here). First-match-wins,
    /// same order as before extraction: DoubleListing (single repeated line, then the
    /// generalised k-repeat form) -> EarlyPaymentProgramme -> NegativeNetSignFlip ->
    /// DebtCharge (Session 21: moved after the structural checks and now gated on
    /// Description/CostCentreArea evidence, see HasDebtChargeEvidence) ->
    /// VatRoundingNoise -> Unreconciled (the default).
    /// </summary>
    public static (ScheduleAClassification Classification, string? Detail) ClassifyScheduleA(
        IReadOnlyList<SpendRow> members, decimal netSum, decimal grossStated, decimal diff,
        GrossMeaning grossMeaning, string? vatType, decimal expected)
    {
        // Double listing vs itemisation: only meaningful when every member line is
        // identical (same net, same gross, same description) - i.e. the SAME line
        // appears to have been published more than once, not several genuinely
        // different charges that happen to be numerically equal.
        if (members.Count > 1
            && members.Select(r => (r.Net, r.Gross, (r.Description ?? "").Trim())).Distinct().Count() == 1)
        {
            decimal lineNet = members[0].Net;
            if (lineNet != 0m)
            {
                decimal ratio = netSum / lineNet; // trivially == members.Count, kept explicit
                if (grossMeaning == GrossMeaning.RepeatedInvoiceTotal
                    && ratio >= 2m && Math.Abs(ratio - Math.Round(ratio)) < 0.001m)
                {
                    return (ScheduleAClassification.DoubleListing,
                        $"{members.Count} identical lines of {lineNet:0.00} sum to {ratio:0}x " +
                        $"the stated invoice ({grossStated:0.00}) - the line appears to have been " +
                        "published more than once, not itemised into separate charges.");
                }
            }
        }

        // Generalised double listing (Session 14): every distinct (Net,Gross,Description)
        // tuple in the group appears the SAME number of times (k >= 2), and one copy of
        // each distinct tuple's Net reconciles the stated Gross exactly.
        if (members.Count > 1 && grossMeaning == GrossMeaning.RepeatedInvoiceTotal)
        {
            var tupleGroups = members
                .GroupBy(r => (r.Net, r.Gross, (r.Description ?? "").Trim()))
                .ToList();
            var distinctCounts = tupleGroups.Select(tg => tg.Count()).Distinct().ToList();
            if (tupleGroups.Count >= 2 && distinctCounts.Count == 1 && distinctCounts[0] >= 2)
            {
                int k = distinctCounts[0];
                decimal oneCopyNet = tupleGroups.Sum(tg => tg.Key.Net);
                if (Math.Abs(oneCopyNet - grossStated) <= 0.01m && Math.Abs(netSum - k * grossStated) <= 0.01m)
                {
                    return (ScheduleAClassification.DoubleListing,
                        $"{tupleGroups.Count} distinct lines summing to the stated invoice " +
                        $"({grossStated:0.00}) exactly are each repeated {k} times ({members.Count} " +
                        "lines total) - the whole itemised invoice appears to have been published " +
                        $"{k} times, not itemised once.");
                }

                // Session 26: the same shape when the stated Gross is VAT-inclusive. One copy of each distinct line,
                // uplifted at its VAT Type, reconciles the stated Gross (Wokingham FY2020-21 trans 3557249, REDS10:
                // 4 lines = 2 x (3,147.16 + 558,320.02) = 2 x 561,467.18; 561,467.18 x 1.2 = 673,760.62 = the stated
                // Gross; trans 3558633, Balfour Beatty: 6 distinct lines each twice, one copy x 1.2 = 592,072.14
                // against 592,072.13). The Gross is then Net/k x the VAT multiple, which is why it read 0.6 x Net.
                var oneCopy = tupleGroups.Select(tg => tg.First()).ToList();
                var oneCopyUplifted = HasMixedVatLabels(oneCopy)
                    ? ClosestTo(grossStated, PerLineExpectedGross(oneCopy, false), VatAdjustedExpectedGross(oneCopyNet, oneCopy[0].VatType))
                    : VatAdjustedExpectedGross(oneCopyNet, oneCopy[0].VatType);
                if (oneCopyUplifted != Math.Round(oneCopyNet, 2, MidpointRounding.AwayFromZero)
                    && Math.Abs(oneCopyUplifted - grossStated) <= 0.05m)
                {
                    return (ScheduleAClassification.DoubleListing,
                        $"{tupleGroups.Count} distinct lines are each repeated {k} times ({members.Count} lines total); " +
                        $"one copy ({oneCopyNet:0.00}) uplifted at its VAT Type is {oneCopyUplifted:0.00}, the stated invoice " +
                        $"({grossStated:0.00}) - the whole itemised invoice appears to have been published {k} times, " +
                        "not itemised once.");
                }
            }
        }

        // Early-payment-programme batch (Session 12): every member line carries the
        // council's own "Early payment programme" cost-centre label, each negative.
        if (members.Count > 1
            && members.All(m => m.Net < 0m)
            && members.All(m => (m.CostCentreArea ?? "").Trim().Equals("Early payment programme", StringComparison.OrdinalIgnoreCase)))
        {
            return (ScheduleAClassification.EarlyPaymentProgramme,
                $"{members.Count} negative per-invoice discount-fee lines under the council's own " +
                "\"Early payment programme\" cost centre - a supply-chain-finance discount batch " +
                "(e.g. Oxygen Finance Ltd), not an arithmetic mismatch. The stated Gross is the " +
                "batch's published total; the underlying invoice amounts are not in this file.");
        }

        // Negative-net sign flip (Session 13): the published Net is negative; flipping
        // ONLY its sign reconciles the stated Gross exactly under either comparison.
        if (netSum < 0m && grossStated > 0m)
        {
            decimal flippedExpected = HasMixedVatLabels(members)
                ? ClosestTo(grossStated, PerLineExpectedGross(members, flipSign: true), VatAdjustedExpectedGross(-netSum, vatType))
                : VatAdjustedExpectedGross(-netSum, vatType);
            decimal flippedRaw = Math.Round(-netSum, 2, MidpointRounding.AwayFromZero);
            bool reconcilesUplifted = Math.Abs(grossStated - flippedExpected) <= 0.01m;
            bool reconcilesRaw = Math.Abs(grossStated - flippedRaw) <= 0.01m;
            if (reconcilesUplifted || reconcilesRaw)
            {
                return (ScheduleAClassification.NegativeNetSignFlip,
                    $"published Net ({netSum:0.00}) is the exact negative of the figure needed to " +
                    $"reconcile the stated Gross ({grossStated:0.00}) under {(reconcilesUplifted ? $"VAT Type {vatType}" : "a raw, VAT-independent")} " +
                    "comparison - the magnitude and VAT arithmetic are both correct, only the sign of " +
                    "the published Net is inverted. Not established why; reported as a named, recurring " +
                    "shape, not an explained one.");
            }
        }

        // Debt-charge pattern (Session 21 fix - coordinator-reported bug): the stated amount
        // decomposes as an exact round principal plus a (non-round) interest/charge
        // remainder, AND the source data itself names this as debt (Description or
        // CostCentreArea mentions "Debt Charges"/"Interest"/"Loan" - the real confirmed
        // cases, Wandsworth/Oxfordshire inter-authority loans, are explicitly labelled this
        // way in the raw file). Moved to run AFTER DoubleListing/EarlyPaymentProgramme/
        // NegativeNetSignFlip (the structural checks), and gated on this evidence, because
        // the amount-shape-only version was misclassifying Wokingham's own Optalis Limited
        // ("TPP - Inter Company"/"Inter Company Income" - its own adult-social-care arm's-
        // length company, not a lender) rows as DebtCharge purely by coincidence on large
        // round-ish amounts: trans 3558420 (14 lines, 7 distinct amounts doubled - a
        // DoubleListing shape the VAT-mixed uplift currently can't confirm, so it correctly
        // falls through to Unreconciled now, not a false "explained" DebtCharge); trans
        // 3920573 and 3614883/3614885 (sign flips - now correctly caught by
        // NegativeNetSignFlip above, which this reorder lets run first). A false "explained"
        // tag hiding a real inter-company recharge is worse than an unreconciled row, same
        // reasoning TryDecomposeDebtCharge's own doc comment already applies to the amount
        // shape alone.
        if (HasDebtChargeEvidence(members) && TryDecomposeDebtCharge(grossStated, out var principal, out var interest))
        {
            return (ScheduleAClassification.DebtCharge,
                $"{grossStated:0.00} decomposes as principal {principal:0.00} + interest/charge " +
                $"{interest:0.00}, and the source data itself names this as a debt/interest charge " +
                "(Description or Cost Centre Area) - a debt-charge-style invoice, not an " +
                "unexplained mismatch.");
        }

        // VAT rounding noise (Session 12): a gap of a few pence under the VAT-type-uplifted
        // comparison, consistent with per-line VAT rounding.
        if (Math.Abs(diff) <= 0.05m)
        {
            return (ScheduleAClassification.VatRoundingNoise,
                $"stated gross {grossStated:0.00} is only {Math.Abs(diff):0.00} from the VAT-type-" +
                $"uplifted expected {expected:0.00} - consistent with per-line VAT rounding, not a " +
                "genuine unexplained gap.");
        }

        return (ScheduleAClassification.Unreconciled, null);
    }

    /// <summary>
    /// Schedule R's classification cascade (Session 20: extracted out of <see cref="Run"/>,
    /// zero behaviour change, same reason as <see cref="ClassifyScheduleA"/>). First-match-
    /// wins: a direct same-supplier/matching-magnitude charge, then a same-voucher 2-line
    /// split, then the Unmatched default.
    /// </summary>
    public static (CreditMatchClassification Classification, string Detail) ClassifyScheduleR(
        SpendRow credit, SpendRow? match, SpendRow? splitMatchA, SpendRow? splitMatchB)
    {
        if (match is not null)
            return (CreditMatchClassification.MatchedOffsettingCharge,
                $"offsetting charge found: {match.TransactionId} ({match.InvoiceType}) on " +
                $"{match.PayDateRaw}, amount {match.Gross:0.00}, within 365 days, same supplier.");
        if (splitMatchA is not null)
            return (CreditMatchClassification.MatchedOffsettingCharge,
                $"offsetting charge found as a SAME-VOUCHER 2-line split: {splitMatchA.TransactionId} " +
                $"amount {splitMatchA.Gross:0.00} + {splitMatchB!.TransactionId} amount {splitMatchB.Gross:0.00} " +
                $"(same Voucher Number as this credit, {credit.TransactionId}) sum to this credit's magnitude " +
                "exactly - a multi-line settlement, not a single-line offset.");
        return (CreditMatchClassification.Unmatched,
            "no same-supplier charge of matching magnitude found in the loaded data within " +
            "365 days - the matching charge may simply be in a file not loaded this run; not " +
            "established to be an error, reported as published.");
    }

    /// <summary>
    /// Debt-charge pattern: an invoiced amount that decomposes exactly into an exact round
    /// principal plus a non-round interest/charge remainder (a loan repayment, a debt-
    /// recovery fee, a late-payment interest charge). Tries round bases from a million
    /// down to 500 only - deliberately NOT finer bases (100, 50, 10): flooring to a fine
    /// base always leaves a small remainder by construction (at most the base itself), so
    /// a fine base would trivially "explain" almost every ordinary mismatch above a few
    /// hundred pounds as a false "debt charge", not just real ones. The large bases
    /// (1,000,000 / 500,000 / 100,000) exist because real inter-authority treasury
    /// lending shows exactly this shape at that scale (found by hand-checking real
    /// Wokingham data, Session 9: trans 3676987, Wandsworth Borough Council, net
    /// 23,934.25 + principal exactly 10,000,000 = gross 10,023,934.25, VAT type NBUS -
    /// before this fix the smaller 1000-base caught the same row but mis-reported the
    /// principal as 10,023,000, a true but misleadingly ungenerous read of a cleanly
    /// round 10-million loan). Requires a principal of at least 100 (a real debt/loan,
    /// not a rounding artefact on a small amount) and accepts the FIRST (largest) base
    /// where the leftover reads like genuine accrued interest/fee: positive, non-trivial
    /// (not itself a round number - that would just be two round fees, not interest), and
    /// a small fraction of the principal (under 5% - a real interest/charge component on
    /// a debt is a small minority of the total, not a sizeable chunk of it). Deliberately
    /// conservative: a false "explained" tag on a real exception is worse than missing a
    /// true debt-charge invoice. NOTE: an earlier version of this also skipped any
    /// remainder that was itself a round number (reasoning: "two round fees look like
    /// double-counting, not interest"). Hand-checking real Wokingham data (Session 9)
    /// disproved that: trans 3669612, Oxfordshire County Council, description literally
    /// "Interest Payments", is genuinely a GBP 5,000,000 principal plus a clean, round
    /// GBP 82,500 interest charge (a plausible clean percentage rate on a round sum) -
    /// the old round-remainder check produced a false negative on a case the source data
    /// itself names as an interest payment. The ratio cap below (5%) does the real work
    /// of rejecting "two round fees" shapes; the separate round-number check was removed.
    /// </summary>
    /// <summary>
    /// Session 21 fix: DebtCharge requires the source data itself to name this as debt -
    /// not just an amount that happens to decompose into a round-ish principal plus a
    /// small remainder. Checked against the GROUP's own Description and CostCentreArea
    /// (whichever field the council actually uses - Wokingham's confirmed real DebtCharge
    /// cases, Wandsworth/Oxfordshire inter-authority loans, both carry Description "Debt
    /// Charges"). Deliberately a narrow keyword match (debt/interest/loan), not a lender-
    /// name allowlist - a real debt-charge invoice can be in the data under many
    /// supplier names (any council, any bank), but genuine debt/loan-interest invoices
    /// are named as such in the council's own published category, while routine
    /// inter-company recharges (Optalis's own "TPP - Inter Company"/"Inter Company
    /// Income") never are.
    /// </summary>
    private static bool HasDebtChargeEvidence(IReadOnlyList<SpendRow> members)
    {
        string text = string.Join(" ", members
            .SelectMany(m => new[] { m.Description, m.CostCentreArea })
            .Where(s => !string.IsNullOrWhiteSpace(s)))
            .ToUpperInvariant();
        return text.Contains("DEBT") || text.Contains("INTEREST") || text.Contains("LOAN");
    }

    public static bool TryDecomposeDebtCharge(decimal amount, out decimal principal, out decimal interest)
    {
        principal = 0m; interest = 0m;
        if (amount <= 0m) return false;
        foreach (var basePound in new[] { 1_000_000m, 500_000m, 100_000m, 1000m, 500m })
        {
            decimal candidatePrincipal = Math.Floor(amount / basePound) * basePound;
            if (candidatePrincipal < 100m) continue;
            decimal candidateInterest = amount - candidatePrincipal;
            if (candidateInterest <= 0.01m) continue; // amount IS the round number - no charge to find.
            if (candidateInterest >= candidatePrincipal * 0.05m) continue; // too large a fraction to read as interest.
            principal = candidatePrincipal;
            interest = candidateInterest;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The VAT uplift a published VAT-TYPE code itself asserts, and nothing else. Only
    /// applied when the net figure was published directly (VatType field in use); when
    /// net was derived from Gross-VatAmount this is not needed (the subtraction already
    /// used the real, per-row VAT amount).
    /// EXEM/NBUS/OSCP/ZERO: no uplift. RRTE: 5%. STD: 20%. Anything else/blank: expect
    /// gross == net (the only honest default with no VAT information at all).
    /// </summary>
    /// <summary>A transaction id in scientific notation, e.g. "2.02408E+11" or "2.50001E+13".</summary>
    public static bool IsLossyTransactionId(string? id) =>
        !string.IsNullOrEmpty(id) && System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d(\.\d+)?[eE]\+\d+$");

    /// <summary>True when the group's lines carry more than one distinct non-blank VAT Type label.</summary>
    public static bool HasMixedVatLabels(IReadOnlyList<SpendRow> members) =>
        members.Select(m => (m.VatType ?? "").Trim().ToUpperInvariant())
               .Where(v => v.Length > 0).Distinct().Count() > 1;

    /// <summary>
    /// For a mixed-label group two readings of the council's VAT are possible and both occur: each line at its own
    /// label's rate, or the whole net at the first line's rate (Wokingham Network Healthcare, trans 3765708: Standard
    /// 1,003.38 + Exempt 214.62 stated 1,461.60 = 1,218.00 x 1.2, i.e. the whole at Standard). The expectation used is
    /// whichever reading is closer to the stated Gross, so a group that reconciles under either is not flagged.
    /// </summary>
    private static decimal ClosestTo(decimal target, decimal a, decimal b) =>
        Math.Abs(target - a) <= Math.Abs(target - b) ? a : b;

    /// <summary>Sum over lines of each line's net uplifted at its OWN VAT Type label's rate.</summary>
    public static decimal PerLineExpectedGross(IReadOnlyList<SpendRow> members, bool flipSign) =>
        members.Sum(m => VatAdjustedExpectedGross(flipSign ? -m.Net : m.Net, m.VatType));

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
