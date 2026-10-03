// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\StandingSchedule.cs, re-synced 2026-10-03 (Session 25/26 sync): NEW (Session 26): standing monthly payment recognition for Schedule B / R.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>
/// Session 26: standing-payment recognition for Schedule B. Found by hand on Reading's Brighter Futures for Children
/// rows: seven fixed amounts (GBP 7,410,659 in all) are paid once a month, month after month; in Feb 2024 the whole
/// block appears three times because Dec and Jan have none and Nov has two. Counting payments against months elapsed,
/// not just spotting equal amounts, is what separates "paid late, then caught up" from "paid twice".
///
/// A (supplier, net) key is a STANDING payment when it appears in at least six distinct calendar months and at least
/// 60% of those months carry exactly one transaction. For a Schedule B group dated in month M, with F the first month
/// the key appears and T = min(M + 3, last month the key appears):
///   payments(T) = transactions of that key in months F..T that are not credit/refund typed,
///   refunds(T)  = credit/refund-typed transactions of the same supplier and the same amount in F..T,
///   surplus     = payments(T) - refunds(T) - months(F..T).
/// surplus &lt;= 0 : the extra payments are made good by months with none (late then caught up, paid ahead, or paid
///                 twice then refunded) - StandingScheduleCatchUp, a consistent reading, not a proof.
/// surplus  &gt; 0 : more payments than months, and nothing returned - StandingScheduleSurplus, reported as published.
/// </summary>
public static class StandingSchedule
{
    public sealed record Series(int FirstMonth, int LastMonth, Dictionary<int, int> Payments, Dictionary<int, int> Refunds)
    {
        /// <summary>Empty months between the end of this supplier's previous standing amount and this series' first month
        /// (0 if no predecessor within 12 months). A new yearly rate often starts with backdated instalments paid on one
        /// day (Reading -> Thames Valley Police, July 2024: three payments on 4 July for April-June).</summary>
        public int PredecessorGap { get; init; }
    }

    private static int MonthIndex(DateOnly d) => d.Year * 12 + (d.Month - 1);

    /// <summary>Builds the (supplier, net) month series from transaction-level representatives (Net already summed per transaction).</summary>
    public static Dictionary<(string Supplier, decimal Net), Series> Build(IEnumerable<SpendRow> transactions, Func<SpendRow, bool> isCredit)
    {
        var tmp = new Dictionary<(string, decimal), (Dictionary<int, int> pay, Dictionary<int, int> refund)>();
        foreach (var r in transactions)
        {
            var d = r.PayDate ?? AuditEngine.ParseDate(r.PayDateRaw);
            if (d is null || r.Net == 0m) continue;
            bool credit = isCredit(r);
            var key = (r.Supplier, Math.Abs(r.Net));
            if (!tmp.TryGetValue(key, out var e)) tmp[key] = e = (new Dictionary<int, int>(), new Dictionary<int, int>());
            var dict = credit ? e.refund : e.pay;
            int m = MonthIndex(d.Value);
            dict[m] = dict.GetValueOrDefault(m) + 1;
        }
        var result = new Dictionary<(string, decimal), Series>();
        foreach (var (key, e) in tmp)
        {
            if (e.pay.Count < 6) continue;
            if (e.pay.Values.Count(c => c == 1) < 0.6 * e.pay.Count) continue;
            // LastMonth also covers refund months: a refund after the last payment is part of the same story.
            result[key] = new Series(e.pay.Keys.Min(), Math.Max(e.pay.Keys.Max(), e.refund.Count > 0 ? e.refund.Keys.Max() : 0), e.pay, e.refund);
        }
        // Predecessor gap per supplier: the nearest earlier series of a DIFFERENT amount that ended within 12 months.
        var bySupplier = result.GroupBy(kv => kv.Key.Item1).ToList();
        foreach (var sg in bySupplier)
            foreach (var kv in sg)
            {
                int bestGap = -1;
                foreach (var other in sg)
                {
                    if (other.Key.Item2 == kv.Key.Item2) continue;
                    int lastPay = other.Value.Payments.Keys.Max();
                    int gap = kv.Value.FirstMonth - lastPay - 1;
                    if (gap >= 0 && gap <= 12 && (bestGap < 0 || gap < bestGap)) bestGap = gap;
                }
                if (bestGap > 0) result[kv.Key] = kv.Value with { PredecessorGap = bestGap };
            }
        return result;
    }

    /// <summary>
    /// Credit/refund-typed rows whose (supplier, amount) recurs monthly UNDER THE REFUND LABEL itself (at least four
    /// distinct months, at least 60% of them exactly one row). Reading pays Brighter Futures for Children every month
    /// in fixed amounts (4,343,592 / 2,326,567 / 1,315,893 / 374,745 / 184,921 for May-Oct 2024; 4,740,050 /
    /// 2,818,908 / 374,744 / 184,916 / 100,533 for May-Sep 2025) and, from March 2024, records them with Invoice Type
    /// "RBC Refunds Manual Entry"; the Schedule R test (find a charge of the same size) can never match a fixed
    /// monthly transfer that is itself typed as a refund. Returns the detail text per key.
    /// </summary>
    public static Dictionary<(string Supplier, decimal Amount), string> BuildCreditSeries(IEnumerable<SpendRow> credits)
    {
        var tmp = new Dictionary<(string, decimal), Dictionary<int, int>>();
        foreach (var r in credits)
        {
            var d = r.PayDate ?? AuditEngine.ParseDate(r.PayDateRaw);
            // POSITIVE refund-typed rows only: that is Reading's 2024+ convention, where the refund label sits on rows
            // that read as payments out. A negative CREDIT row is a credit note against a charge; a credit that repeats
            // monthly (PAYCOLL, ALLPAY.NET) is a different thing and is left to the ordinary matching.
            if (d is null || r.Gross <= 0m) continue;
            var key = (r.Supplier, Math.Abs(r.Gross));
            if (!tmp.TryGetValue(key, out var months)) tmp[key] = months = new Dictionary<int, int>();
            int m = MonthIndex(d.Value);
            months[m] = months.GetValueOrDefault(m) + 1;
        }
        var result = new Dictionary<(string, decimal), string>();
        foreach (var (key, months) in tmp)
        {
            if (months.Count < 4) continue;
            if (months.Values.Count(c => c == 1) < 0.6 * months.Count) continue;
            result[key] = $"the same amount recurs under the refund Invoice Type in {months.Count} months " +
                          $"({Label(months.Keys.Min())} to {Label(months.Keys.Max())}), usually once a month - a fixed recurring " +
                          "transfer that carries the refund label, not a one-off refund awaiting a charge of the same size. " +
                          "What the council means by the label is not stated in the file.";
        }
        return result;
    }

    public static (ScheduleBClassification Classification, string Detail)? Classify(
        string supplier, decimal net, DateOnly groupDate, int groupSize, IReadOnlyDictionary<(string Supplier, decimal Net), Series> index)
    {
        if (!index.TryGetValue((supplier, Math.Abs(net)), out var s)) return null;
        int m = MonthIndex(groupDate);
        if (m < s.FirstMonth || m > s.LastMonth) return null;
        int t = Math.Min(m + 3, s.LastMonth);
        // A group in the first three months of a series may be the backdated start of a new rate: the empty months
        // since the supplier's previous amount ended can absorb it.
        int bonus = m - s.FirstMonth <= 2 ? s.PredecessorGap : 0;
        int months = t - s.FirstMonth + 1 + bonus;
        int payments = s.Payments.Where(kv => kv.Key <= t).Sum(kv => kv.Value);
        int refunds = s.Refunds.Where(kv => kv.Key >= s.FirstMonth && kv.Key <= t).Sum(kv => kv.Value);
        int surplus = payments - refunds - months;
        string where = $"{s.Payments.Count} months of {s.LastMonth - s.FirstMonth + 1} carry this amount, usually once";
        if (surplus <= 0)
            return (ScheduleBClassification.StandingScheduleCatchUp,
                $"standing payment ({where}): {payments} payments" + (refunds > 0 ? $" less {refunds} refunded" : "") +
                $" over the {months} months from {Label(s.FirstMonth - bonus)} to {Label(t)}" +
                (bonus > 0 ? $" (counting the {bonus} empty months since this supplier's previous amount ended)" : "") +
                $", so the {groupSize} on this date are made good by " +
                "months with none (late then caught up, paid ahead, or repaid) - consistent with a catch-up, not proof of one.");
        return (ScheduleBClassification.StandingScheduleSurplus,
            $"standing payment ({where}): {payments} payments" + (refunds > 0 ? $" less {refunds} refunded" : "") +
            $" over {months} months from {Label(s.FirstMonth)} to {Label(t)}, a surplus of {surplus} not returned in the data.");
    }

    private static string Label(int m) => $"{m / 12}-{(m % 12) + 1:00}";
}
