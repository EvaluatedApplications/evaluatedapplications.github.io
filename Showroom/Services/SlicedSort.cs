namespace Showroom.Services;

/// <summary>
/// A stable merge sort that hands the thread back to the browser between slices (see <see cref="Cooperative"/>). Sorting 63,000
/// rows with LINQ's OrderBy is one block of tens of milliseconds on a fast desktop and several times that on a phone; here each
/// slice sorts a 1,024-row run or merges a couple of thousand rows, so the page keeps answering taps while it works.
/// Stable, like OrderBy: rows that compare equal keep their original order (a descending sort passes the reversed comparison).
/// </summary>
public static class SlicedSort
{
    const int Run = 1024, MergeSlice = 2048;

    public static async Task<List<T>> SortAsync<T>(List<T> source, Comparison<T> compare, Cooperative co)
    {
        int n = source.Count;
        if (n < 2) return new List<T>(source);
        var a = source.ToArray();
        var b = new T[n];

        // 1) sort each run on its own (stable: ties broken by position)
        var idx = new int[Run];
        var tmp = new T[Run];
        for (int s = 0; s < n; s += Run)
        {
            int len = Math.Min(Run, n - s);
            for (int i = 0; i < len; i++) idx[i] = i;
            int start = s;
            Array.Sort(idx, 0, len, Comparer<int>.Create((x, y) =>
            {
                int c = compare(a[start + x], a[start + y]);
                return c != 0 ? c : x.CompareTo(y);
            }));
            for (int i = 0; i < len; i++) tmp[i] = a[s + idx[i]];
            Array.Copy(tmp, 0, a, s, len);
            await co.YieldIfDueAsync();
        }

        // 2) merge neighbouring runs, doubling the width, in slices
        for (int width = Run; width < n; width *= 2)
        {
            int outPos = 0, sinceYield = 0;
            for (int lo = 0; lo < n; lo += 2 * width)
            {
                int mid = Math.Min(lo + width, n), hi = Math.Min(lo + 2 * width, n);
                int i = lo, j = mid;
                while (i < mid && j < hi)
                {
                    // take from the left on ties: that is what keeps it stable
                    b[outPos++] = compare(a[j], a[i]) < 0 ? a[j++] : a[i++];
                    if (++sinceYield >= MergeSlice) { sinceYield = 0; await co.YieldIfDueAsync(); }
                }
                while (i < mid) b[outPos++] = a[i++];
                while (j < hi) b[outPos++] = a[j++];
            }
            (a, b) = (b, a);
            await co.YieldIfDueAsync();
        }
        return new List<T>(a);
    }
}
