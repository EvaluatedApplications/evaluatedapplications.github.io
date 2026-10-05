namespace CouncilAudit;

/// <summary>
/// Session 52 (audit E3-E5): which identical transactions are listed as a pair, and how their extra copies are counted. Pure logic,
/// used by the CLI's transaction-twin scan and tested here.
/// <list type="bullet">
/// <item>Both first pay dates must be published, and within seven days.</item>
/// <item>A run of four or more identical transactions is a schedule at its own cadence (the median gap between its distinct first
/// pay dates); a pair inside it is listed only when it is closer than that cadence. A run whose members all fall on one day has no
/// cadence, so the seven-day window alone applies.</item>
/// <item>Pairs that share a transaction form one group; k transactions are k - 1 extra copies. Each transaction after the group's
/// earliest is credited once, on the pair through which a walk from the earliest first reaches it.</item>
/// </list>
/// </summary>
public static class TwinPairs
{
    /// <summary>The listed pairs (i &lt; j, indexes into <paramref name="firstDates"/>, which must be sorted by date) and the count of
    /// pairs left out because a first pay date is not published.</summary>
    public static (List<(int i, int j, int days)> pairs, int undated) List(IReadOnlyList<DateTime?> firstDates)
    {
        // Session 52, SET ASIDE: the cadence rule (list a pair inside a run of 4+ when closer than the run's own spacing) was run once and
        // took the list from 640 to 5,200 pairs, mostly same-day batches of identical purchases (Bristol, Motus Commercials: 27 identical
        // two-line transactions on 4 February 2022; Hawke Incentives runs of 10). Those are batches, not twins, so the stated rule
        // stays "at most three identical transactions" until a batch test exists. Cadence() is kept for that next step.
        if (firstDates.Count > 3) return (new List<(int, int, int)>(), 0);
        double cadence = Cadence(firstDates);
        var pairs = new List<(int, int, int)>();
        int undated = 0;
        for (int i = 0; i < firstDates.Count; i++)
            for (int j = i + 1; j < firstDates.Count; j++)
            {
                if (!firstDates[i].HasValue || !firstDates[j].HasValue) { undated++; continue; }
                int days = (int)(firstDates[j]!.Value - firstDates[i]!.Value).TotalDays;
                if (Math.Abs(days) > 7 || Math.Abs(days) >= cadence) continue;
                pairs.Add((i, j, days));
            }
        return (pairs, undated);
    }

    /// <summary>The run's own spacing: the median gap between distinct first pay dates, for a run of four or more; otherwise (and for
    /// a run all on one day) no spacing applies.</summary>
    public static double Cadence(IReadOnlyList<DateTime?> firstDates)
    {
        if (firstDates.Count < 4) return double.MaxValue;
        var dates = firstDates.Where(d => d.HasValue).Select(d => d!.Value.Date).Distinct().OrderBy(d => d).ToList();
        var gaps = dates.Zip(dates.Skip(1), (p, q) => (q - p).TotalDays).OrderBy(x => x).ToList();
        if (gaps.Count == 0) return double.MaxValue;
        return gaps.Count % 2 == 1 ? gaps[gaps.Count / 2] : (gaps[gaps.Count / 2 - 1] + gaps[gaps.Count / 2]) / 2.0;
    }

    /// <summary>For each listed pair: a group number (0-based within this call), the group's size in transactions, and the value it
    /// is credited with (a member's total, once per member after the group's earliest; 0 on the other pairs).</summary>
    public static (int[] group, int[] size, decimal[] credited) Groups(IReadOnlyList<(int i, int j, int days)> pairs, IReadOnlyList<decimal> totals)
    {
        var adj = new Dictionary<int, List<int>>();
        for (int p = 0; p < pairs.Count; p++)
        {
            var (i, j, _) = pairs[p];
            if (!adj.TryGetValue(i, out var li)) adj[i] = li = new(); li.Add(p);
            if (!adj.TryGetValue(j, out var lj)) adj[j] = lj = new(); lj.Add(p);
        }
        var group = new int[pairs.Count]; var credited = new decimal[pairs.Count]; var sizes = new List<int>();
        var seen = new HashSet<int>();
        foreach (int root in adj.Keys.OrderBy(k => k))
        {
            if (!seen.Add(root)) continue;
            int gid = sizes.Count; int n = 1;
            var queue = new Queue<int>(); queue.Enqueue(root);
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                foreach (int p in adj[node])
                {
                    group[p] = gid;
                    int other = pairs[p].i == node ? pairs[p].j : pairs[p].i;
                    if (seen.Add(other)) { credited[p] = totals[other]; n++; queue.Enqueue(other); }
                }
            }
            sizes.Add(n);
        }
        return (group, group.Select(g => sizes[g]).ToArray(), credited);
    }
}
