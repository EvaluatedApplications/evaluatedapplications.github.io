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
        => MapRows(table, map, sourceTag, warnings, null);

    /// <summary>Session 54 (re-audit N3): every scanner-made placeholder for a missing transaction number is written "(no number published) N", the form the
    /// page labels: "(no number) N" becomes "(no number published) N" and "(no number: CHAPS) N" becomes "(no number published) N CHAPS" (the method the
    /// council printed in the number column is kept after the row number). A real number is returned unchanged.</summary>
    public static string NormalisePlaceholderId(string id)
    {
        if (!id.StartsWith("(no number", StringComparison.Ordinal) || id.StartsWith("(no number published)", StringComparison.Ordinal)) return id;
        var m = System.Text.RegularExpressions.Regex.Match(id, @"^\(no number(?:: ([^)]*))?\) (\d+)$");
        if (!m.Success) return id;
        return "(no number published) " + m.Groups[2].Value + (m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? " " + m.Groups[1].Value : "");
    }

    /// <summary>Session 52: false reproduces the old behaviour (a payment with a blank number is dropped, but now logged); used once to
    /// measure the "before" side of PREREG_S52_AUDIT_FIXES.md. Production is true.</summary>
    public static bool KeepUnnumberedPayments = true;

    /// <summary>The Net a row maps to (the same derivation <see cref="MapRows(IReadOnlyList{string[]}, ColumnMapping, string, List{string}, List{DroppedRow}?)"/> uses).</summary>
    static decimal RowNet(string net, string gross, string? vat, bool hasNet) =>
        hasNet ? ParseMoney(net) : vat is not null ? ParseMoney(gross) - ParseMoney(vat) : ParseMoney(gross);

    /// <summary>
    /// Session 52 (audit E1): the mapping used to skip every row whose transaction-number cell was blank, silently. Reading's
    /// monthly CSVs leave the Voucher Number blank on real payments (HMRC remittances, CHAPS and Faster Payments, rents,
    /// capital fees: 2,021 rows, about GBP 69.1m, July 2021 to August 2026), so those payments never reached the export.
    /// Now a row is dropped only when it holds no payee AND no payment date (a blank line or an amount-only footer), and
    /// every dropped row is written to <paramref name="dropped"/> with its reason and amount, so a reconciliation can account
    /// for every record. A row with a payee or a date but no number is a payment: it gets its own placeholder number
    /// "(no number) N" (N = its row position in the table), the convention the prepared councils already use, so it is never
    /// grouped with an unrelated payment.
    /// </summary>
    public static List<SpendRow> MapRows(
        IReadOnlyList<string[]> table, ColumnMapping map, string sourceTag, List<string> warnings, List<DroppedRow>? dropped)
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
            if (f.All(string.IsNullOrWhiteSpace))
            {
                // a fully blank line is not a record; counted, never mapped
                dropped?.Add(new DroppedRow(sourceTag, r - 1, "blank row", 0m));
                continue;
            }
            string Get(int idx) => idx >= 0 && idx < f.Length ? f[idx] : "";

            if (iTrans >= 0 && string.IsNullOrWhiteSpace(Get(iTrans)))
            {
                bool hasPayee = iSupplier >= 0 && !string.IsNullOrWhiteSpace(Get(iSupplier));
                bool hasDate = iPayDate >= 0 && !string.IsNullOrWhiteSpace(Get(iPayDate));
                if (!hasPayee && !hasDate)
                {
                    dropped?.Add(new DroppedRow(sourceTag, r - 1, "no number, no payee and no payment date (a footer or sub-total line)",
                        RowNet(Get(iNet), Get(iGross), iVatAmt >= 0 ? Get(iVatAmt) : null, iNet >= 0)));
                    continue;
                }
                if (!KeepUnnumberedPayments)
                {
                    dropped?.Add(new DroppedRow(sourceTag, r - 1, "UNNUMBERED PAYMENT (blank transaction number, has a payee or a date)",
                        RowNet(Get(iNet), Get(iGross), iVatAmt >= 0 ? Get(iVatAmt) : null, iNet >= 0)));
                    continue;
                }
                var copy = new string[Math.Max(f.Length, iTrans + 1)];
                for (int c = 0; c < copy.Length; c++) copy[c] = c < f.Length ? f[c] : "";
                // Session 54 (re-audit N3): written "(no number published) N", the placeholder every other council uses and the page already labels
                copy[iTrans] = "(no number published) " + (r - 1).ToString(CultureInfo.InvariantCulture);
                f = copy;
            }

            // Session 54 (re-audit N4, privacy): a row is redacted if ANY column carries a redaction marker, not only the payee column the
            // mapping names. Bradford's August 2026 file has two payee columns; "SupplierName" said REDACTED PERSONAL DATA on 229 childcare-voucher
            // rows while "Supplier Name" held the person's name, and the scanner published it. Every name-keyed cell of such a row is replaced by the
            // marker here, so no name of a redacted row reaches any export, slice or cross file (see RowRedaction).
            if (RowRedaction.MarkerIn(header, f, iSupplier, RowRedaction.AnyColumn) is not null)
            {
                var withheld = new List<string>();
                f = RowRedaction.Withhold(header, f, iSupplier, RowRedaction.Label(header, f, iSupplier), withheld);
                RowRedaction.Record(sourceTag, withheld);
            }

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
                NormalisePlaceholderId(Get(iTrans).Trim()),
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
    public static bool IsRedactedPlaceholder(string s) =>
        (s.Contains("REDACT", StringComparison.OrdinalIgnoreCase)
            && !s.Contains("REDACTIVE", StringComparison.OrdinalIgnoreCase)) // Session 43: Redactive Media Group (Redactive Publishing, Redactive Events) is a real supplier in 22 of the 24 councils
        || (s.StartsWith("RED", StringComparison.OrdinalIgnoreCase)
            && s.EndsWith("ACTED", StringComparison.OrdinalIgnoreCase)
            && s.Length >= 8);

    /// <summary>
    /// Session 43: a payee that is a redaction placeholder in a spelling the plain test misses. Surrey writes its placeholder "Redated Personal Data" (1,722 lines), "Redcated Personal Data" (532)
    /// and "Dedacted Personal Data" (12); Wirral "REDACETED PERSONAL DATA" (7) and "REEDACTED PERSONAL DATA" (1); all contain "personal data". Surrey also writes a missing name out as "Null" (99).
    /// A payee label that contains "personal data" or is the word NULL is a withheld or missing name, so it is excluded and counted as a redacted row, exactly as "REDACTED" is. Applied to the
    /// payee name only, never to a transaction number. Cornwall has 37 lines it also reaches ("Personal Data", "A2B Taxis St Austell- personal data"). See PREREG_S43_SURREY.md Amendment 1.
    /// </summary>
    public static bool IsRedactedSupplier(string s) =>
        IsRedactedPlaceholder(s)
        || s.Contains("PERSONAL DATA", StringComparison.OrdinalIgnoreCase)
        || s.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase)
        || IsExcludePlaceholder(s);

    /// <summary>Session 45: Stockport's October 2025 file writes the payee "*Exclude" (131 lines) and "*Exclude - VAT only" (32): the council's own label, not a name, shared by unrelated payments
    /// (it made the only Schedule D transaction, a Bedspace Resource Ltd run plus one -19.78 line). Only these two labels, compared after punctuation is removed ("*Exclude" and its key "EXCLUDE").
    /// See PREREG_S45_STOCKPORT.md Amendment 1.</summary>
    internal static bool IsExcludePlaceholder(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool space = true;
        foreach (char c in s) { if (char.IsLetterOrDigit(c)) { sb.Append(char.ToUpperInvariant(c)); space = false; } else if (!space) { sb.Append(' '); space = true; } }
        string t = sb.ToString().Trim();
        return t == "EXCLUDE" || t == "EXCLUDE VAT ONLY";
    }
    public static AuditResult Run(
        IReadOnlyList<SpendRow> all,
        TransactionIdScope idScope = TransactionIdScope.CouncilWideUnique,
        GrossMeaning grossMeaning = GrossMeaning.RepeatedInvoiceTotal,
        List<string>? warnings = null)
    {
        warnings ??= new List<string>();

        var redactedCount = all.Count(r => IsRedactedPlaceholder(r.TransactionId) || IsRedactedSupplier(r.Supplier));
        // Session 52 (audit E11): a redacted line can sit under a real transaction number next to named lines (Wokingham 11 November
        // 2022: 3766535 carries 129 named lines and one "Redacted Personal Details" line of 350.00). The schedules leave the redacted line
        // out, so a transaction-level detail must say it exists, or the stated Gross looks further from the lines than it is.
        var redactedInTxn = all.Where(r => !IsRedactedPlaceholder(r.TransactionId) && IsRedactedSupplier(r.Supplier))
            .GroupBy(r => (r.SourceTag, r.TransactionId)).ToDictionary(g => g.Key, g => (Lines: g.Count(), Net: g.Sum(r => r.Net)));
        if (redactedCount > 0)
        {
            warnings.Add(
                $"{redactedCount} rows carry a redacted transaction id or supplier name (safeguarding/personal-data " +
                "withholding) and are excluded from every schedule below: the placeholder is shared across unrelated " +
                "payments, so grouping by it produces false matches, not real discrepancies.");
            all = all.Where(r => !IsRedactedPlaceholder(r.TransactionId) && !IsRedactedSupplier(r.Supplier)).ToList();
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
        var nSquared = new Dictionary<(string, string), (NSquaredListing.Result Shape, decimal Gross)>();
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
                // Session 41: the n-squared print shape (see NSquaredListing). Exact reconciliation of the de-duplicated lines
                // is a labelled publication defect; the shape alone keeps the row Unreconciled and states the de-duplicated value.
                var sq = NSquaredListing.Analyse(tm);
                if (sq is not null) nSquared[(t.Key.SourceTag, t.Key.TransactionId)] = (sq, tGross);
                redactedInTxn.TryGetValue((t.Key.SourceTag, t.Key.TransactionId), out var red);
                bool sqExact = sq is not null && NSquaredListing.Reconciles(sq, tGross, red.Net);
                // Session 54 (re-audit N2): an n-squared run that stays Unreconciled showed the gap against the n-squared ROW total (Wokingham 3766535:
                // about -728,548 where the real shortfall is 19,250.00). The figures of such a row are now those of the de-duplicated lines, and the gap is
                // the stated Gross minus those lines minus the transaction's redacted lines (stated in the detail).
                bool sqDedup = false;
                if (sq is not null && !sqExact && ComputeScheduleAGroupInputs(NSquaredListing.Deduplicate(tm), grossMeaning) is { } dIn)
                {
                    (tNet, tGross, tExp, tDiff) = dIn; tDiff -= red.Net; sqDedup = true;
                }
                string tDetail = $"multi-payee transaction: {tm.Count} lines to {nSup} named suppliers sum to {tNet:0.00} (VAT-uplifted {tExp:0.00}), " +
                    $"but the one Gross repeated on every line is {tGross:0.00}. Tested once as a whole transaction because a " +
                    "per-supplier comparison against a transaction-wide figure cannot reconcile." + NSquaredListing.RedactedNote(red.Lines, red.Net);
                if (sq is not null) tDetail = sqExact ? NSquaredListing.Describe(sq, tGross, red.Lines, red.Net)
                    : sqDedup ? "multi-payee transaction, figures and gap taken on the de-duplicated lines (expected " + tExp.ToString("0.00") + " for the named lines): " + NSquaredListing.Describe(sq, tGross, red.Lines, red.Net)
                    : tDetail + " Also: " + NSquaredListing.Describe(sq, tGross, red.Lines, red.Net);
                scheduleA.Add(new ScheduleARow(
                    tm[0].SourceTag, tm[0].TransactionId, $"{tm[0].Supplier} (+{nSup - 1} other payees)", tm[0].PayDateRaw,
                    // Session 52 (audit E2): Difference = stated Gross - VAT-adjusted expected Gross, not the VAT itself
                    tNet, tGross, tExp, tDiff, tm[0].VatType, tm[0].ServiceArea, tm[0].Description, tm.Count,
                    sqExact ? ScheduleAClassification.NSquaredListing : ScheduleAClassification.Unreconciled,
                    tDetail,
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
            // in a separate benchmark harness (not in this repository), NOT wired in here).
            var (classification, detail) = ClassifyScheduleA(members, netSum, grossStated, diff, grossMeaning, first.VatType, expected);

            scheduleA.Add(new ScheduleARow(
                first.SourceTag, first.TransactionId, first.Supplier, first.PayDateRaw,
                netSum, grossStated, expected, diff, // Session 52 (audit E2): was grossStated - netSum, which is the VAT, not the gap
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
        var cadenceIndex = new Lazy<Dictionary<(string Supplier, string Description), CadenceCatchUp.Stream>>(() =>
            CadenceCatchUp.Build(withPayDate.Select(x => x.Rep), r => IsCreditLikeInvoiceType(r.InvoiceType) || r.Net < 0m));
        var batchIndex = new Lazy<Dictionary<(string Supplier, decimal Net), RecurringBatches.Info>>(() =>
            RecurringBatches.Build(withPayDate.Select(x => x.Rep), r => IsCreditLikeInvoiceType(r.InvoiceType) || r.Net < 0m));
        // Negative transactions by (supplier, pay date, description, the POSITIVE amount they cancel), counted.
        var reversalIndex = new Lazy<Dictionary<(string, string, string, decimal), int>>(() =>
        {
            var d = new Dictionary<(string, string, string, decimal), int>();
            foreach (var x in withPayDate.Where(x => x.Rep.Net < 0m))
            {
                var k = (x.Rep.Supplier, x.Rep.PayDateRaw ?? "", (x.Rep.Description ?? "").Trim(), -x.Rep.Net);
                d[k] = d.GetValueOrDefault(k) + 1;
            }
            return d;
        });
        foreach (var g in dupGroups)
        {
            groupId++;
            var ordered = g.OrderBy(x => x.Rep.SourceTag).ThenBy(x => x.Rep.TransactionId, StringComparer.Ordinal).ToList();
            var members = ordered.Select(x => x.Rep).ToList();
            var memberRowIndexes = ordered.Select(x => x.RowIndexes).ToList();

            // Same supplier invoice number repeated = likely duplicate; different invoice
            // numbers at what is otherwise an identical amount = a standardised recurring
            // rate (checklist 4b, confirmed on real Reading data: [individual's name withheld] group repeats
            // one amount under TWO DIFFERENT invoice numbers - a legitimate recurring rent
            // payment; [individual's name withheld] and Lynx Lettings's groups repeat the SAME invoice number
            // twice each - a genuine duplicate-payment candidate). Redacted rows never reach
            // here (excluded above), so a redacted payee is never compared this way.
            var invoiceNumbers = members.Select(m => m.SupplierInvoiceNumber).ToList();
            var reading = ScheduleBReading.None; string? readingMeaning = null;
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

            // Session 30: copies cancelled the same day. Leeds 19 June 2017 posts the monthly Secretary of State settlement
            // (Business Rates Tariffs -5,660,668.00, Revenue Support Grant -5,851,504.00, NNDR Income 16,217,763.00) twice
            // and then posts the whole set once more with every sign reversed, so two copies less one reversal is one payment.
            // Applied AFTER the standing-payment test and only to groups still open (Unclear, or a standing-payment surplus), so a
            // group the standing test already explained keeps that label and every earlier tier definition built on the old
            // labels (the pre-registered T_B tier) is unchanged.
            if (classification is ScheduleBClassification.Unclear or ScheduleBClassification.StandingScheduleSurplus
                && !members.Any(m => IsCreditLikeInvoiceType(m.InvoiceType) || m.Net < 0m)
                && reversalIndex.Value.TryGetValue((g.Key.Item1, g.Key.Item5 ?? "", g.Key.Item4, g.Key.Item2), out int reversals)
                && members.Count - reversals <= 1)
            {
                classification = ScheduleBClassification.ReversedSameDay;
                detail = $"{members.Count} copies and {reversals} equal-and-opposite line(s) from the same supplier with the same description on the same date: {members.Count - reversals} payment(s) net.";
            }

            // Session 31: the council's own label names a ledger-migration control account on every line (see the enum member).
            // Applied last and only to groups still open, so no earlier label moves.
            if (classification is ScheduleBClassification.Unclear or ScheduleBClassification.StandingScheduleSurplus
                && members.All(m => HasMigrationControlLabel(m)))
            {
                classification = ScheduleBClassification.MigrationControlLabel;
                detail = $"every one of the {members.Count} lines carries the council's own label \"{(HasLabel(members[0].Description) ? members[0].Description : members[0].CostCentreArea)}\", a ledger-migration control account; the file does not say whether cash left under it.";
            }

            // Session 33: the (supplier, amount) forms same-day batches on many separate dates. Last, only for groups still Unclear.
            if (classification == ScheduleBClassification.Unclear
                && !members.Any(m => IsCreditLikeInvoiceType(m.InvoiceType) || m.Net < 0m)
                && batchIndex.Value.TryGetValue((g.Key.Item1, g.Key.Item2), out var batchInfo)
                && RecurringBatches.Explain(batchInfo, g.Key.Item5 ?? "", members.Count) is { } batchDetail)
            {
                reading = ScheduleBReading.RecurringBatchRate;
                readingMeaning = batchDetail;
            }

            // Session 33: a monthly stream whose amount changed; payments this month are no more than one plus the empty months before.
            if (classification == ScheduleBClassification.Unclear && reading == ScheduleBReading.None
                && !members.Any(m => IsCreditLikeInvoiceType(m.InvoiceType) || m.Net < 0m)
                && g.Key.Item2 > 0m
                && (members[0].PayDate ?? ParseDate(members[0].PayDateRaw)) is { } cadenceDate
                && CadenceCatchUp.Explain(cadenceIndex.Value, g.Key.Item1, g.Key.Item4, cadenceDate) is { } cadenceDetail)
            {
                reading = ScheduleBReading.CadenceCatchUp;
                readingMeaning = cadenceDetail;
            }

            // Session 41: the group's transaction is an n-squared multi-payee run: its published Net is the inflated row total.
            // A flag only, applied when nothing else read the group; the class stays Unclear and the group stays counted.
            if (classification == ScheduleBClassification.Unclear && reading == ScheduleBReading.None
                && members.Any(m => nSquared.ContainsKey((m.SourceTag, m.TransactionId))))
            {
                var fm = members.First(m => nSquared.ContainsKey((m.SourceTag, m.TransactionId)));
                var q0 = nSquared[(fm.SourceTag, fm.TransactionId)];
                reading = ScheduleBReading.NSquaredRows;
                readingMeaning = "this transaction is a multi-payee run printed n-squared; its published Net of " + q0.Shape.RowNet.ToString("0.00") +
                    " is the inflated row total. " + NSquaredListing.Describe(q0.Shape, q0.Gross,
                        redactedInTxn.TryGetValue((fm.SourceTag, fm.TransactionId), out var redB) ? redB.Lines : 0, redB.Net);
            }

            scheduleB.Add(new ScheduleBGroup(groupId, g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Key.Item4, g.Key.Item5,
                members, classification, detail, memberRowIndexes, reading, readingMeaning));
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
            // Session 27: payment-level netting. A council pays a supplier one payment (one "Payment Number") for
            // a run of invoices less its credit notes, and publishes every line of it. A credit whose own payment
            // (same supplier, file, pay date and Payment Number) carries positive lines at least as large in total
            // was settled inside that payment, whatever invoice it nominally reverses.
            var paymentTotals = new Lazy<Dictionary<(string, string, string, string), (decimal Net, decimal Positive, int Lines)>>(
                () => BuildPaymentTotals(all));
            // Session 27: a file that publishes no negative line at all (Reading from 2024-03: 0 negatives in 98,543
            // rows) carries no credit notes, so a positive refund-typed line there is a payment made through the
            // council's refund channel (133 service areas use it: housing advice, disabled facilities grants, venue
            // settlements, rent refunds), not a credit awaiting its charge. Matching it to a charge asks the wrong
            // question. Small ones are classed as such; one of GBP 100,000 or more stays a lead.
            var tagsWithNegatives = all.Where(r => r.Gross < 0m).Select(r => r.SourceTag).ToHashSet();
            foreach (var c in creditLike)
            {
                var (match, splitMatchA, splitMatchB) = FindScheduleRMatches(c, chargesBySupplier);

                // Session 20: extract-method only (zero behaviour change) - see ClassifyScheduleR
                // below, the twin of ClassifyScheduleA, both ported to an EvalApp 3.0.0 Classify
                // cascade and benchmarked in a separate benchmark harness (not in this repository), NOT wired in here.
                var (classification, detail) = ClassifyScheduleR(c, match, splitMatchA, splitMatchB);
                // Session 26: an unmatched "refund" whose amount recurs monthly under the refund label is a fixed
                // recurring transfer recorded with that label (Reading -> Brighter Futures for Children from 2024-03).
                if (classification == CreditMatchClassification.Unmatched && c.Gross > 0m
                    && creditStanding.Value.TryGetValue((c.Supplier, Math.Abs(c.Gross)), out var standingDetail))
                {
                    classification = CreditMatchClassification.StandingPaymentUnderRefundType;
                    detail = standingDetail;
                }
                if (classification == CreditMatchClassification.Unmatched && c.Gross < 0m
                    && PaymentNumberOf(c) is { } pn
                    && paymentTotals.Value.TryGetValue((c.SourceTag, c.Supplier, c.PayDateRaw ?? "", pn), out var pay)
                    && pay.Net >= -0.01m && pay.Positive >= -c.Gross - 0.01m)
                {
                    classification = CreditMatchClassification.NettedInSamePayment;
                    detail = $"Payment Number {pn} to this supplier on {c.PayDateRaw} carries {pay.Lines} published lines " +
                             $"summing to {pay.Net:0.00} (positive lines {pay.Positive:0.00}): the credit of {c.Gross:0.00} was " +
                             "settled inside the same payment as the charges it reduces. Which invoice it reverses is not stated.";
                }
                if (classification == CreditMatchClassification.Unmatched && c.Gross > 0m && c.Gross < RefundChannelLeadThreshold
                    && !tagsWithNegatives.Contains(c.SourceTag))
                {
                    classification = CreditMatchClassification.RefundChannelPayment;
                    detail = $"positive {c.Gross:0.00} under the refund Invoice Type in a file ({c.SourceTag}) that publishes no negative " +
                             "line at all: a payment made through the council's refund channel, not a credit awaiting its charge. " +
                             "What it was for is in its own Service Area and Description.";
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

    /// <summary>Refund-channel payments from this size up stay Unmatched leads (Session 27).</summary>
    public const decimal RefundChannelLeadThreshold = 100_000m;

    /// <summary>The "Payment Number" a council publishes among its unmapped columns (Reading), else null.</summary>
    public static string? PaymentNumberOf(SpendRow r)
    {
        const string key = "Payment Number=";
        var o = r.OtherColumns;
        if (string.IsNullOrEmpty(o)) return null;
        int i = o.StartsWith(key, StringComparison.Ordinal) ? 0 : o.IndexOf("|" + key, StringComparison.Ordinal) + 1;
        if (i <= 0 && !o.StartsWith(key, StringComparison.Ordinal)) return null;
        int s = i + key.Length;
        int e = o.IndexOf('|', s);
        var v = (e < 0 ? o.Substring(s) : o.Substring(s, e - s)).Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>Net, positive total and line count per (file, supplier, pay date, Payment Number).</summary>
    public static Dictionary<(string, string, string, string), (decimal Net, decimal Positive, int Lines)> BuildPaymentTotals(
        IEnumerable<SpendRow> rows)
    {
        var d = new Dictionary<(string, string, string, string), (decimal, decimal, int)>();
        foreach (var r in rows)
        {
            if (PaymentNumberOf(r) is not { } pn) continue;
            var k = (r.SourceTag, r.Supplier, r.PayDateRaw ?? "", pn);
            d.TryGetValue(k, out var t);
            d[k] = (t.Item1 + r.Gross, t.Item2 + (r.Gross > 0m ? r.Gross : 0m), t.Item3 + 1);
        }
        return d.ToDictionary(kv => kv.Key, kv => (kv.Value.Item1, kv.Value.Item2, kv.Value.Item3));
    }

    /// <summary>
    /// Schedule R's match search (Session 20: extracted out of <see cref="Run"/>, zero
    /// behaviour change - lets a separate benchmark harness (not in this repository)
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
        // [individual's name withheld], SIGNIS). The other 115/125 cross-voucher
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
    /// a separate benchmark harness (not in this repository) can build real (netSum, grossStated, expected, diff)
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
    /// a separate benchmark harness (not in this repository), not wired into that pipeline here). First-match-wins,
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

        // Session 52 (audit E2): a gap under one pound after the VAT uplift (Matrix SCM 3763717: Net 105,245.41, STD, Gross 126,294.86
        // against 126,294.49 expected, 37p) is not a sum to ask a council to explain. It is not called rounding either (one line cannot
        // round by 37p); it is its own class, so the Unreconciled list holds only gaps of a pound or more. See PREREG_S52_AUDIT_FIXES.md.
        if (Math.Abs(diff) < 1.00m)
        {
            return (ScheduleAClassification.SmallGap,
                $"stated gross {grossStated:0.00} is {Math.Abs(diff):0.00} from the VAT-type-uplifted expected {expected:0.00}: " +
                "a gap of under one pound.");
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

    private static bool HasLabel(string? s) => s is not null && s.Contains("MIGRATION CONTROL", StringComparison.OrdinalIgnoreCase);
    internal static bool HasMigrationControlLabel(SpendRow r) => HasLabel(r.Description) || HasLabel(r.CostCentreArea);

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
        // Session 31: "dd/MM/yyyy HH:mm" (Liverpool's CSVs print "06/08/2024 00:00"; without it the next fallback reads it as US
        // month/day, i.e. June 8, silently, whenever the day is 12 or less) and "yyyyMMdd" (Bradford's 2025 files print 20250313).
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "dd-MMM-yy", "d-MMM-yy", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "yyyyMMdd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return DateOnly.FromDateTime(dt2);
        return null;
    }
}
