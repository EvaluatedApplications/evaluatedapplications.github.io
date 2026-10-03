namespace Showroom.Services;

public enum SupplierSearchMode { Closest, Substring }

/// <summary>One supplier a council paid, as the search shows it.</summary>
public sealed record SupplierHit(string Name, string CouncilKey, int Rows, decimal Net, double Score);

/// <summary>
/// The supplier directory the name search runs on: one entry per (council, normalised supplier name) with the number
/// of payment rows and the total paid, plus the name's letter-triple (trigram) set. A council's 300,000 payment rows
/// hold a few thousand distinct suppliers, so a search compares the typed name with a few thousand short sets, not with
/// every row, and finishes in milliseconds.
///
/// "Closest" ranks by cosine similarity of the trigram sets (1.0 = identical name, 0 = nothing in common), which keeps a
/// small spelling difference ("ACME LTD" against "ACME LTD.") or a truncated name near the top. It replaces a HoloDb
/// NEAREST query over the payment rows: that query builds its index on first use at roughly a millisecond per ROW
/// (368 s for Reading's 327,000 rows on a desktop, written up for holodb-owner in MonoRepo HoloDb todo), which froze phones.
/// </summary>
public sealed class SupplierIndex
{
    public sealed class Entry
    {
        public string Council = "", Key = "", Name = "";
        public int Rows; public double Net;
        internal int[] Tri = Array.Empty<int>();
    }

    /// <summary>One council's entries, so a loader adds payment rows without building a composite key per row.</summary>
    public sealed class Bucket
    {
        readonly SupplierIndex _owner; readonly string _council;
        readonly Dictionary<string, Entry> _byKey = new(StringComparer.Ordinal);
        internal Bucket(SupplierIndex owner, string council) { _owner = owner; _council = council; }

        /// <summary>Add one payment (or, with rows/net, a pre-counted aggregate) for a supplier.</summary>
        public void Add(string key, string name, double net, int rows = 1)
        {
            if (!_byKey.TryGetValue(key, out var e))
            {
                e = new Entry { Council = _council, Key = key, Name = name, Tri = Trigrams(key) };
                _byKey[key] = e; _owner._entries.Add(e);
            }
            e.Rows += rows; e.Net += net;
        }
        public int Count => _byKey.Count;
    }

    readonly List<Entry> _entries = new();
    readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public int Count => _entries.Count;
    public Bucket For(string council)
    {
        if (!_buckets.TryGetValue(council, out var b)) _buckets[council] = b = new Bucket(this, council);
        return b;
    }
    public bool Has(string council) => _buckets.ContainsKey(council);

    /// <summary>Same rule CouncilDbBuilder uses for supplierkey: trim, upper-case, keep letters, digits and spaces.</summary>
    public static string Normalise(string s) =>
        new string(s.Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

    /// <summary>Distinct, sorted three-character windows of "  NAME " (two leading spaces, one trailing).</summary>
    internal static int[] Trigrams(string key)
    {
        int n = key.Length;
        var set = new HashSet<int>();
        for (int i = -2; i < n; i++)
        {
            int a = i < 0 ? ' ' : key[i];
            int b = i + 1 < 0 ? ' ' : (i + 1 < n ? key[i + 1] : ' ');
            int c = i + 2 < n ? key[i + 2] : ' ';
            set.Add((a * 31 + b) * 31 + c);
        }
        var arr = set.ToArray(); Array.Sort(arr); return arr;
    }

    static double Cosine(int[] a, int[] b)
    {
        int i = 0, j = 0, common = 0;
        while (i < a.Length && j < b.Length)
        {
            int d = a[i] - b[j];
            if (d == 0) { common++; i++; j++; } else if (d < 0) i++; else j++;
        }
        return common == 0 ? 0 : common / Math.Sqrt((double)a.Length * b.Length);
    }

    /// <summary>The best matches for a typed name, in time slices (a few thousand comparisons per slice) and cancellable.</summary>
    public async Task<List<SupplierHit>> SearchAsync(string query, SupplierSearchMode mode, int take, Cooperative co)
    {
        var q = Normalise(query);
        var result = new List<SupplierHit>();
        if (q.Length == 0) return result;
        var qTri = Trigrams(q);
        var top = new List<(Entry E, double Score)>(take + 1);
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            double score;
            if (mode == SupplierSearchMode.Substring)
            {
                if (e.Key.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                score = e.Key.StartsWith(q, StringComparison.Ordinal) ? 1 : 0.5; // a name that STARTS with the text ranks first, then largest total
            }
            else
            {
                score = Cosine(qTri, e.Tri);
                if (score <= 0) continue;
            }
            // keep the best `take`: higher score first, then the larger total paid (so ties are ordered by a stated rule, not by file order)
            int at = top.Count;
            while (at > 0 && (top[at - 1].Score < score || (top[at - 1].Score == score && top[at - 1].E.Net < e.Net))) at--;
            if (at < take) { top.Insert(at, (e, score)); if (top.Count > take) top.RemoveAt(take); }
            if ((i & 1023) == 1023) await co.YieldIfDueAsync();
        }
        foreach (var (e, s) in top) result.Add(new SupplierHit(e.Name, e.Council, e.Rows, (decimal)Math.Round(e.Net, 2), mode == SupplierSearchMode.Closest ? s : 0));
        return result;
    }
}
