// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\AuditEngine.cs, copied 2026-10-03,
// re-synced 2026-10-03 (same day, CouncilAudit Session 12/13/14: classification work, generalised
// DoubleListing, and Schedule R credit/refund matching).
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
        int iInvoiceNo = Idx(map.SupplierInvoiceNumber);
        int iCostCentre = Idx(map.CostCentreArea);
        int iInvoiceType = Idx(map.InvoiceType);

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
                r - 1,
                iInvoiceNo >= 0 ? Get(iInvoiceNo).Trim() : null,
                iCostCentre >= 0 ? Get(iCostCentre).Trim() : null,
                iInvoiceType >= 0 ? Get(iInvoiceType).Trim() : null));
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
            // Domestic reverse charge (construction services, since March 2021): a "standard
            // rate" (STD) invoice can legitimately show gross == net with NO VAT uplift at all
            // (the customer, not the supplier, accounts for the VAT). Measured (Session 4, real
            // Wokingham construction invoices up to £1.44m): applying the VAT-type uplift
            // unconditionally manufactured ~296 false mismatches of exactly this shape. Only
            // flag when the discrepancy survives under BOTH the VAT-uplifted AND the raw-net
            // (no uplift at all) comparison - if either reconciles, there's nothing to report.
            decimal rawDiff = grossStated - Math.Round(netSum, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(diff) > 0.01m && Math.Abs(rawDiff) <= 0.01m)
                continue; // reverse charge (or any no-uplift case): raw net already reconciles.
            if (Math.Abs(diff) <= 0.01m)
                continue; // VAT-uplifted comparison already reconciles.

            // ---- Classify before reporting, so the schedule doesn't drown real exceptions ----
            var classification = ScheduleAClassification.Unreconciled;
            string? detail = null;

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
                    // Under RepeatedInvoiceTotal, the stated Gross is supposed to be the ONE
                    // true invoice total; genuine itemisation of several equal-value items
                    // would show that true (larger) total as Gross on every line, not the
                    // single line's own value - so an exact integer multiple here (>=2) is the
                    // double-listing signature, not itemisation (which would show diff ~ 0 and
                    // never reach this code at all).
                    if (grossMeaning == GrossMeaning.RepeatedInvoiceTotal
                        && ratio >= 2m && Math.Abs(ratio - Math.Round(ratio)) < 0.001m)
                    {
                        classification = ScheduleAClassification.DoubleListing;
                        detail = $"{members.Count} identical lines of {lineNet:0.00} sum to {ratio:0}x " +
                                 $"the stated invoice ({grossStated:0.00}) - the line appears to have been " +
                                 "published more than once, not itemised into separate charges.";
                    }
                }
            }

            // Generalised double listing (Session 14, Net=2xGross census on Wokingham's
            // remaining Unreconciled population): the check above only catches a SINGLE
            // line published twice. Hand-opened 5 real cases (trans 3556053, 3557512,
            // 3561522, 3557414, 3557664 - all FY2020-21, all EXEM VAT) where the invoice
            // is itself itemised into 2 genuinely DIFFERENT lines (different amounts
            // and/or descriptions) that together sum to the stated Gross exactly - and
            // then that whole 2-line itemisation is published AGAIN, making a 4-line
            // group whose net sum is exactly 2x the stated Gross. Generalises to k
            // repeats of n distinct lines: every distinct (Net, Gross, Description)
            // tuple in the group appears the SAME number of times (k >= 2), and the sum
            // of one copy of each distinct tuple's Net reconciles the stated Gross
            // exactly (raw comparison - every real instance found is EXEM/no VAT
            // uplift; not yet tested against a VAT-uplifted instance, so this check is
            // deliberately narrow to the no-uplift case for now). Full population:
            // 23 of 1,112 rows going into this cycle, all FY2020-21, all 4-line, 100%
            // EXEM VAT Purchases Exempt, 15 distinct suppliers (Request Nursing & Care,
            // The Link Nursing & Care Agency Ltd, Purley Park Trust, Forest Care
            // Limited, Reading & Wokingham Coaches and others) - a real, repeating,
            // year-concentrated (FY2020-21 only, so far) shape, not a one-off.
            if (classification == ScheduleAClassification.Unreconciled
                && members.Count > 1
                && grossMeaning == GrossMeaning.RepeatedInvoiceTotal)
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
                        classification = ScheduleAClassification.DoubleListing;
                        detail = $"{tupleGroups.Count} distinct lines summing to the stated invoice " +
                                 $"({grossStated:0.00}) exactly are each repeated {k} times ({members.Count} " +
                                 "lines total) - the whole itemised invoice appears to have been published " +
                                 $"{k} times, not itemised once.";
                    }
                }
            }

            // Debt-charge pattern: the stated amount is an exact round principal plus a
            // (non-round) interest/charge remainder - a loan/debt-recovery-style invoice,
            // not an unexplained gap. Checked against whichever published figure the
            // mismatch is actually about (grossStated).
            if (classification == ScheduleAClassification.Unreconciled
                && TryDecomposeDebtCharge(grossStated, out var principal, out var interest))
            {
                classification = ScheduleAClassification.DebtCharge;
                detail = $"{grossStated:0.00} decomposes as principal {principal:0.00} + interest/charge " +
                         $"{interest:0.00} - a debt-charge-style invoice, not an unexplained mismatch.";
            }

            // Early-payment-programme batch (Session 12 judgement-sample finding,
            // confirmed real on Wokingham: Oxygen Finance Ltd and other suppliers' own
            // negative discount-fee lines, 3,730 raw rows across six years, all tagged
            // with the council's own "Early payment programme" COST CENTRE AREA - NOT its
            // Description, which is the generic "Fees"; a first manual read of the raw
            // columns got this backwards and was only caught by re-running the real engine
            // against the real header, the exact lesson Session 7 already named once).
            // More than one member line, every line negative, every line's CostCentreArea
            // is exactly this council-published label. Not an arithmetic claim - we cannot
            // and do not reconcile the stated Gross from these lines, only recognise the
            // shape and name it rather than leaving it as an undifferentiated Unreconciled
            // row. Falls back to never matching for a council with no CostCentreArea mapped.
            if (classification == ScheduleAClassification.Unreconciled
                && members.Count > 1
                && members.All(m => m.Net < 0m)
                && members.All(m => (m.CostCentreArea ?? "").Trim().Equals("Early payment programme", StringComparison.OrdinalIgnoreCase)))
            {
                classification = ScheduleAClassification.EarlyPaymentProgramme;
                detail = $"{members.Count} negative per-invoice discount-fee lines under the council's own " +
                         "\"Early payment programme\" cost centre - a supply-chain-finance discount batch " +
                         "(e.g. Oxygen Finance Ltd), not an arithmetic mismatch. The stated Gross is the " +
                         "batch's published total; the underlying invoice amounts are not in this file.";
            }

            // Negative-net sign flip (Session 13 judgement-sample finding, confirmed real
            // on Wokingham: 2,383 of the 3,479 rows that were still Unreconciled going
            // into this cycle - a dedicated census of the opposite-sign shape named in
            // Session 9/10's smaller samples, widened per the Showroom owner's own flag
            // that at least one row shows a negative ExpectedGross against a positive
            // Gross). The published Net is negative; flipping ONLY its sign (same
            // magnitude) reconciles the stated Gross exactly, under either the VAT-type
            // uplift or the raw (no-uplift) comparison already used above - i.e. the
            // magnitude is correct and the VAT arithmetic is correct, only the sign of
            // the one published figure, Net, is inverted. Spans hundreds of distinct
            // suppliers, every VAT type, and a sharply growing count year over year
            // (FY2020-21: 61 -> FY2025-26: 735) - not one supplier's bug and not shrinking,
            // so this is named and separated out, NOT marked "explained": nothing here
            // establishes WHY Net's sign is inverted (a ledger debit/credit convention is
            // the obvious guess, but guessing is not evidence), so it is reported as a
            // named, recurring shape for the council to answer, exactly as Session 9 first
            // insisted for the single-line version of this same pattern - widening the
            // rule to the general form does not change that this stays a real question,
            // not a resolved one.
            if (classification == ScheduleAClassification.Unreconciled && netSum < 0m && grossStated > 0m)
            {
                decimal flippedExpected = VatAdjustedExpectedGross(-netSum, first.VatType);
                decimal flippedRaw = Math.Round(-netSum, 2, MidpointRounding.AwayFromZero);
                bool reconcilesUplifted = Math.Abs(grossStated - flippedExpected) <= 0.01m;
                bool reconcilesRaw = Math.Abs(grossStated - flippedRaw) <= 0.01m;
                if (reconcilesUplifted || reconcilesRaw)
                {
                    classification = ScheduleAClassification.NegativeNetSignFlip;
                    detail = $"published Net ({netSum:0.00}) is the exact negative of the figure needed to " +
                             $"reconcile the stated Gross ({grossStated:0.00}) under {(reconcilesUplifted ? $"VAT Type {first.VatType}" : "a raw, VAT-independent")} " +
                             "comparison - the magnitude and VAT arithmetic are both correct, only the sign of " +
                             "the published Net is inverted. Not established why; reported as a named, recurring " +
                             "shape, not an explained one.";
                }
            }

            // VAT rounding noise (Session 12 judgement-sample finding, confirmed real on
            // Wokingham: Education Boutique Ltd trans 3991866 and Carrington West Ltd
            // trans 3626256, both off by exactly 0.02 under their own published VAT
            // type): once the VAT-type-uplifted comparison is the one in play (not a
            // reverse-charge or no-VAT row - those already `continue`d above), a gap of a
            // few pence is consistent with VAT being rounded per line then summed rather
            // than once on the total, not a genuine unexplained gap. Deliberately narrow
            // (5p) - Holt School's trans 3928781 in the same sample was off by GBP 4.03
            // under the same reduced rate and correctly stays Unreconciled.
            if (classification == ScheduleAClassification.Unreconciled && Math.Abs(diff) <= 0.05m)
            {
                classification = ScheduleAClassification.VatRoundingNoise;
                detail = $"stated gross {grossStated:0.00} is only {Math.Abs(diff):0.00} from the VAT-type-" +
                         $"uplifted expected {expected:0.00} - consistent with per-line VAT rounding, not a " +
                         "genuine unexplained gap.";
            }

            scheduleA.Add(new ScheduleARow(
                first.SourceTag, first.TransactionId, first.Supplier, first.PayDateRaw,
                netSum, grossStated, expected, grossStated - netSum,
                first.VatType, first.ServiceArea, first.Description, members.Count,
                classification, detail));
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

            scheduleB.Add(new ScheduleBGroup(groupId, g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Key.Item4, g.Key.Item5,
                members, classification, detail));
        }

        // Same ranking principle as Schedule A: groups the rules can't explain either way
        // (Unclear - no invoice number was available to check) rise above the ones already
        // labelled one way or the other, largest value first within each band.
        scheduleB = scheduleB
            .OrderBy(b => b.Classification == ScheduleBClassification.Unclear ? 0
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

            foreach (var c in creditLike)
            {
                SpendRow? match = null;
                if (chargesBySupplier.TryGetValue(c.Supplier, out var charges))
                {
                    match = charges.FirstOrDefault(ch =>
                        Math.Abs(Math.Abs(ch.Gross) - Math.Abs(c.Gross)) <= 0.01m
                        && WithinDateWindow(c.PayDate, ch.PayDate, 365));
                }
                var classification = match is not null
                    ? CreditMatchClassification.MatchedOffsettingCharge
                    : CreditMatchClassification.Unmatched;
                string detail = match is not null
                    ? $"offsetting charge found: {match.TransactionId} ({match.InvoiceType}) on " +
                      $"{match.PayDateRaw}, amount {match.Gross:0.00}, within 365 days, same supplier."
                    : "no same-supplier charge of matching magnitude found in the loaded data within " +
                      "365 days - the matching charge may simply be in a file not loaded this run; not " +
                      "established to be an error, reported as published.";
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
