namespace CouncilAudit;

/// <summary>
/// Session 33: a (supplier, amount) that forms same-day batches again and again. Found by hand on Bracknell Forest's
/// Queensmead School (SEN non-LEA provision): every quarter one date carries 12 or 13 payments of 27,223 and 7 or 8 of
/// 29,549 (and the same two rates uplifted by exactly 5% in later years, 28,584.15 and 31,026.45), the batch sizes
/// drifting 13, 13, 12, 12, 10 as a roll of placements would.
///
/// A (supplier, amount) is a BATCH RATE when it has same-day batches of at least <see cref="MinBatchSize"/> transactions on
/// at least <see cref="MinBatchDates"/> distinct pay dates spread over at least <see cref="MinMonths"/> calendar months. A
/// payment error does not recur on four separate dates; a per-head rate, a roll of placements or a staged purchase does.
/// A Schedule B group of that rate is then explained only if its size is no larger than the largest batch the same rate
/// formed on any OTHER date (leave-one-out): counting against the rate's own norm, so a batch that exceeds every other
/// batch of its rate is not explained and stays open. A first cut that explained every group of a batch rate moved
/// 44% of Wokingham's open rows and 60% of Reading's, and could not tell a usual batch from an extra payment.
/// </summary>
public static class RecurringBatches
{
    public const int MinBatchDates = 4;
    public const int MinMonths = 3;
    /// <summary>Pairs do not define a batch rate: two same-amount payments on a date recur for ordinary reasons (two clients
    /// on one rate).</summary>
    public const int MinBatchSize = 3;

    public sealed record Info(Dictionary<string, int> SizeByDate, int Months, string First, string Last)
    {
        public int BatchDates => SizeByDate.Count;
    }

    public static Dictionary<(string Supplier, decimal Net), Info> Build(IEnumerable<SpendRow> transactions, Func<SpendRow, bool> isCredit)
    {
        var perDate = new Dictionary<(string, decimal, string), (int count, int month)>();
        foreach (var r in transactions)
        {
            var d = r.PayDate ?? AuditEngine.ParseDate(r.PayDateRaw);
            if (d is null || r.Net <= 0m || isCredit(r)) continue;
            var k = (r.Supplier, r.Net, r.PayDateRaw ?? "");
            perDate.TryGetValue(k, out var e);
            perDate[k] = (e.count + 1, d.Value.Year * 12 + d.Value.Month - 1);
        }
        var result = new Dictionary<(string, decimal), Info>();
        foreach (var g in perDate.Where(kv => kv.Value.count >= MinBatchSize).GroupBy(kv => (kv.Key.Item1, kv.Key.Item2)))
        {
            var dates = g.ToList();
            if (dates.Count < MinBatchDates) continue;
            int months = dates.Select(x => x.Value.month).Distinct().Count();
            if (months < MinMonths) continue;
            var ordered = dates.OrderBy(x => x.Value.month).ThenBy(x => x.Key.Item3, StringComparer.Ordinal).ToList();
            result[g.Key] = new Info(dates.ToDictionary(x => x.Key.Item3, x => x.Value.count, StringComparer.Ordinal),
                months, ordered[0].Key.Item3, ordered[^1].Key.Item3);
        }
        return result;
    }

    /// <summary>The explanation if this group (size <paramref name="groupSize"/>, dated <paramref name="payDateRaw"/>) is no larger than
    /// the largest batch of the same rate on another date; null if it is larger or the date is not one of the rate's batch dates
    /// and the group is also a pair or larger than every batch.</summary>
    public static string? Explain(Info i, string payDateRaw, int groupSize)
    {
        int otherMax = i.SizeByDate.Where(kv => !string.Equals(kv.Key, payDateRaw, StringComparison.Ordinal)).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
        if (groupSize > otherMax) return null;
        int others = i.SizeByDate.Count(kv => !string.Equals(kv.Key, payDateRaw, StringComparison.Ordinal));
        return $"this amount forms same-day batches of {i.SizeByDate.Values.Min()} to {i.SizeByDate.Values.Max()} on {i.BatchDates} separate pay dates over {i.Months} months " +
               $"({i.First} to {i.Last}); the {groupSize} here are no more than the {otherMax} that the same amount formed together on another of the {others} dates. " +
               "A payment error does not recur on that many dates; a per-head rate, a roll of placements or a staged purchase does - a consistent reading, not a proof.";
    }
}
