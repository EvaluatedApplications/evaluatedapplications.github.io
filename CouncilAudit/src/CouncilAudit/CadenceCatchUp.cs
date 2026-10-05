namespace CouncilAudit;

/// <summary>
/// Session 33: a monthly stream whose AMOUNT changes. Found by hand on Royal Borough of Windsor and Maidenhead's "Business Rates
/// Tariff" payments to the Department for Communities and Local Government: one payment in most months of 2023 to 2025 (2.65m or
/// 2.98m), then three of 4,887,566.00 in June 2026 (19, 19 and 30 June) after a new yearly rate. The amount-keyed standing test
/// cannot see this (a new amount has no history). The stream (supplier plus the council's own description) can: the same payer, the
/// same line of the ledger, paid about once a month in steady sums. Counting against months elapsed, as the standing test does: the
/// payments in a month are explained when they are no more than one plus the empty months just before it.
///
/// A (supplier, description) stream qualifies when it has at least <see cref="MinActiveMonths"/> active months, they fill at least 60%
/// of the months between its first and last, at least 70% of them carry exactly one payment, and its payment amounts are steady
/// (standard deviation at most <see cref="MaxCoefficientOfVariation"/> of the mean: a fixed fee or tariff, not a care-fee line whose
/// amounts differ every month, where two equal amounts on a day are a coincidence of rates and not a missed month made good).
/// Credit and refund typed rows and negative amounts are not payments. For a group of a qualifying stream dated in month M,
///   payments(M) = all payments of the stream in M, whatever their amount,
///   empty(M)    = consecutive empty months immediately before M (at most <see cref="MaxLookBack"/>),
/// and the group is a CadenceCatchUp when payments(M) &lt;= 1 + empty(M). A consistent reading, not a proof.
/// </summary>
public static class CadenceCatchUp
{
    public const int MinActiveMonths = 8;
    public const int MaxLookBack = 6;
    public const double MinDensity = 0.6;
    public const double MaxCoefficientOfVariation = 0.35;

    public sealed record Stream(int First, int Last, Dictionary<int, int> PaymentsByMonth);

    private static int MonthIndex(DateOnly d) => d.Year * 12 + (d.Month - 1);

    public static Dictionary<(string Supplier, string Description), Stream> Build(IEnumerable<SpendRow> transactions, Func<SpendRow, bool> isCredit)
    {
        var tmp = new Dictionary<(string, string), (Dictionary<int, int> months, List<double> nets)>();
        foreach (var r in transactions)
        {
            var d = r.PayDate ?? AuditEngine.ParseDate(r.PayDateRaw);
            if (d is null || r.Net <= 0m || isCredit(r)) continue;
            var key = (r.Supplier, (r.Description ?? "").Trim());
            if (key.Item2.Length == 0) continue;
            if (!tmp.TryGetValue(key, out var e)) tmp[key] = e = (new Dictionary<int, int>(), new List<double>());
            int m = MonthIndex(d.Value);
            e.months[m] = e.months.GetValueOrDefault(m) + 1;
            e.nets.Add((double)r.Net);
        }
        var result = new Dictionary<(string, string), Stream>();
        foreach (var (key, e) in tmp)
        {
            var months = e.months;
            if (months.Count < MinActiveMonths) continue;
            int span = months.Keys.Max() - months.Keys.Min() + 1;
            if (months.Count < MinDensity * span) continue;
            if (months.Values.Count(c => c == 1) < 0.7 * months.Count) continue;
            double mean = e.nets.Average();
            double sd = Math.Sqrt(e.nets.Sum(x => (x - mean) * (x - mean)) / e.nets.Count);
            if (sd > MaxCoefficientOfVariation * mean) continue;
            result[key] = new Stream(months.Keys.Min(), months.Keys.Max(), months);
        }
        return result;
    }

    public static string? Explain(IReadOnlyDictionary<(string Supplier, string Description), Stream> index, string supplier, string? description, DateOnly groupDate)
    {
        if (!index.TryGetValue((supplier, (description ?? "").Trim()), out var s)) return null;
        int m = MonthIndex(groupDate);
        if (m < s.First || m > s.Last) return null;
        int payments = s.PaymentsByMonth.GetValueOrDefault(m);
        int empty = 0;
        for (int k = m - 1; k >= s.First && empty < MaxLookBack && !s.PaymentsByMonth.ContainsKey(k); k--) empty++;
        if (payments > 1 + empty) return null;
        return $"the supplier's \"{(description ?? "").Trim()}\" line is paid about once a month in steady sums ({s.PaymentsByMonth.Count} active months " +
               $"of {s.Last - s.First + 1}, {s.First / 12}-{s.First % 12 + 1:00} to {s.Last / 12}-{s.Last % 12 + 1:00}); {payments} payments fall in " +
               $"{m / 12}-{m % 12 + 1:00} after {empty} empty month(s), so they are no more than the {1 + empty} due - " +
               "consistent with a catch-up, not proof of one.";
    }
}
