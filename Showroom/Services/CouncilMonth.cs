using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Showroom.Services;

public sealed record ExLine(string Tx, string Supplier, decimal Net, decimal Gross, decimal Diff);

/// <summary>One flagged item as shown on the page: a single row (Schedule A) or the lines of one group (B and D) in the chosen month.
/// Built on demand for the few groups the visitor actually opens, from the month's raw bytes.</summary>
public sealed class ExGroup
{
    public string Schedule = "", Class = "", Detail = "";
    public List<ExLine> Lines = new();     // the first few lines of the group (enough to show; see MonthScan.ShownLines)
    public int LineCount;                  // every line of the group that falls in this month
    public decimal Value;                  // A: the amount that does not reconcile; B and D: the net value of the lines
}

/// <summary>All the groups of one schedule and classification in the month, biggest first.</summary>
public sealed class ExBucket
{
    internal MonthScan Scan = null!;
    internal List<MonthScan.Gr> Items = new();
    public string Schedule = "", Class = "";
    public int Lines;
    public decimal Value;
    public bool SupplierFurnished;
    public int Shown = 15;                 // how many groups the page currently lists (UI state)
    public bool Open;
    public int GroupCount => Items.Count;
    readonly Dictionary<int, ExGroup> _made = new();
    public ExGroup Group(int k)
    {
        if (!_made.TryGetValue(k, out var g)) _made[k] = g = Scan.Materialize(Items[k]);
        return g;
    }
}

/// <summary>
/// Reads one month's exception slice(s) straight from their bytes. A month can hold 18,000 flagged lines, and turning each of the
/// thirteen fields of each line into a string made the page stall on a phone, so the first pass reads only the five fields it needs to
/// group, count and rank (schedule, group id, net, difference, classification), keeps each line's position, and never builds a string
/// per line. The lines of a group are read again, in full, only when the visitor opens that group. The pass runs in time slices.
/// </summary>
public sealed class MonthScan
{
    public const int ShownLines = 12;

    internal sealed class Gr
    {
        public byte Sched;                 // 'A', 'B' or 'D'
        public string Cls = "";
        public int First = -1, Last = -1, Count;
        public double Value;
    }

    List<byte[]> _parts = new();
    readonly List<byte> _rowPart = new();
    readonly List<int> _rowOff = new();
    readonly List<int> _rowNext = new();
    int[] _cols = Array.Empty<int>();
    public List<ExBucket> Buckets { get; private set; } = new();
    public int TotalLines { get; private set; }

    // column positions, by header name
    int cSched, cGid, cTx, cSupplier, cNet, cDiff, cDetail, cClass, cGross;

    public static async Task<MonthScan> ScanAsync(List<byte[]> parts, Cooperative co)
    {
        var s = new MonthScan { _parts = parts };
        var gmap = new Dictionary<long, Gr>();
        var all = new List<Gr>();
        var classes = new Interner();
        var st = new int[32]; var ln = new int[32];
        var chars = new char[64];
        int sinceCheck = 0;
        for (int p = 0; p < parts.Count; p++)
        {
            var b = parts[p];
            int pos = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            int nf = Fields(b, ref pos, st, ln);
            s.Header(b, st, ln, nf);
            while (pos < b.Length)
            {
                int rowStart = pos;
                nf = Fields(b, ref pos, st, ln);
                if (nf < 12) continue;
                byte sched = ln[s.cSched] > 0 ? b[st[s.cSched]] : (byte)'?';
                long gid = 0;
                bool hasGid = ln[s.cGid] > 0 && Utf8Parser.TryParse(b.AsSpan(st[s.cGid], ln[s.cGid]), out gid, out _);
                double net = 0, diff = 0;
                if (ln[s.cNet] > 0) Utf8Parser.TryParse(b.AsSpan(st[s.cNet], ln[s.cNet]), out net, out _);
                if (ln[s.cDiff] > 0) Utf8Parser.TryParse(b.AsSpan(st[s.cDiff], ln[s.cDiff]), out diff, out _);
                int cl = ln[s.cClass];
                if (chars.Length < cl) chars = new char[cl * 2];
                for (int i = 0; i < cl; i++) chars[i] = (char)b[st[s.cClass] + i];
                string cls = classes.Get(chars.AsSpan(0, cl));

                int row = s._rowOff.Count;
                s._rowPart.Add((byte)p); s._rowOff.Add(rowStart); s._rowNext.Add(-1);
                Gr g;
                long key = hasGid && sched != 'A' ? (gid << 2) | (sched == 'B' ? 1L : 2L) : 0;
                if (key == 0 || !gmap.TryGetValue(key, out g!))
                {
                    g = new Gr { Sched = sched, Cls = cls };
                    all.Add(g);
                    if (key != 0) gmap[key] = g;
                }
                if (g.First < 0) g.First = row; else s._rowNext[g.Last] = row;
                g.Last = row; g.Count++;
                g.Value += sched == 'A' ? Math.Abs(diff) : net;
                if (sched == 'D' && ln[s.cDetail] > 0 && g.Count == 1)
                    s._furnished |= b.AsSpan(st[s.cDetail], ln[s.cDetail]).IndexOf("SupplierFurnished"u8) >= 0;

                if (++sinceCheck >= 256) { sinceCheck = 0; if (co.Due) await co.YieldAsync(); }
            }
        }
        s.TotalLines = s._rowOff.Count;

        var buckets = new List<ExBucket>();
        foreach (var g in all)
        {
            ExBucket? bk = null;
            foreach (var x in buckets) if (x.Schedule[0] == (char)g.Sched && (object)x.Class == g.Cls) { bk = x; break; }
            if (bk is null) { bk = new ExBucket { Scan = s, Schedule = ((char)g.Sched).ToString(), Class = g.Cls }; buckets.Add(bk); }
            bk.Items.Add(g); bk.Lines += g.Count; bk.Value += (decimal)g.Value;
        }
        foreach (var bk in buckets)
        {
            bk.Items.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            bk.SupplierFurnished = bk.Schedule == "D" && s._furnished;
            if (co.Due) await co.YieldAsync();
        }
        buckets.Sort((a, b) =>
        {
            int c = SchedOrder(a.Schedule).CompareTo(SchedOrder(b.Schedule));
            if (c != 0) return c;
            c = CouncilTerms.Rank(a.Class).CompareTo(CouncilTerms.Rank(b.Class));
            return c != 0 ? c : Math.Abs(b.Value).CompareTo(Math.Abs(a.Value));
        });
        s.Buckets = buckets;
        return s;
    }

    bool _furnished;
    static int SchedOrder(string s) => s == "B" ? 0 : s == "A" ? 1 : 2;

    void Header(byte[] b, int[] st, int[] ln, int nf)
    {
        int Find(string name)
        {
            for (int i = 0; i < nf; i++)
                if (ln[i] == name.Length && Encoding.ASCII.GetString(b, st[i], ln[i]).Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
        cSched = Find("Schedule"); cGid = Find("GroupId"); cTx = Find("TransactionId"); cSupplier = Find("SupplierName"); cNet = Find("Net");
        cDiff = Find("Difference"); cDetail = Find("Detail"); cClass = Find("Classification"); cGross = Find("TransactionGross");
        if (cSched < 0 || cGid < 0 || cNet < 0 || cDiff < 0 || cClass < 0 || cDetail < 0 || cTx < 0 || cSupplier < 0)
            throw new InvalidDataException("This month's file does not have the columns the page expects.");
        if (cGross < 0) cGross = cNet;
    }

    /// <summary>Reads one record into start/length pairs (the field's bytes, without the surrounding quotes); leaves pos after the line end.</summary>
    static int Fields(byte[] b, ref int pos, int[] st, int[] ln)
    {
        int n = b.Length, f = 0;
        while (true)
        {
            int s, e;
            if (pos < n && b[pos] == (byte)'"')
            {
                s = ++pos;
                while (pos < n) { if (b[pos] == (byte)'"') { if (pos + 1 < n && b[pos + 1] == (byte)'"') { pos += 2; continue; } break; } pos++; }
                e = pos; if (pos < n) pos++;                 // the closing quote
                while (pos < n && b[pos] != (byte)',' && b[pos] != (byte)'\n') pos++;
            }
            else
            {
                s = pos;
                while (pos < n && b[pos] != (byte)',' && b[pos] != (byte)'\n') pos++;
                e = pos;
                if (e > s && b[e - 1] == (byte)'\r') e--;
            }
            if (f < st.Length) { st[f] = s; ln[f] = e - s; f++; }
            if (pos >= n) return f;
            byte t = b[pos++];
            if (t == (byte)'\n') return f;
        }
    }

    string Text(byte[] b, int s, int l)
    {
        if (l <= 0) return "";
        var t = Encoding.UTF8.GetString(b, s, l);
        return t.Contains("\"\"") ? t.Replace("\"\"", "\"") : t;
    }

    static decimal Dec(string s) => decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    /// <summary>Reads a group's first few lines in full (every field), for display.</summary>
    internal ExGroup Materialize(Gr g)
    {
        var o = new ExGroup { Schedule = ((char)g.Sched).ToString(), Class = g.Cls, LineCount = g.Count, Value = (decimal)g.Value };
        var st = new int[32]; var ln = new int[32];
        int row = g.First;
        for (int i = 0; i < ShownLines && row >= 0; i++, row = _rowNext[row])
        {
            var b = _parts[_rowPart[row]];
            int pos = _rowOff[row];
            int nf = Fields(b, ref pos, st, ln);
            if (nf < 12) continue;
            if (i == 0) o.Detail = Text(b, st[cDetail], ln[cDetail]);
            o.Lines.Add(new ExLine(Text(b, st[cTx], ln[cTx]), Text(b, st[cSupplier], ln[cSupplier]),
                Dec(Text(b, st[cNet], ln[cNet])), Dec(Text(b, st[cGross], ln[cGross])), Dec(Text(b, st[cDiff], ln[cDiff]))));
        }
        return o;
    }

    /// <summary>The engine's own explanation sits after its machine prefix ("groupSize=3;", "idScope=...;", "expected=..."): keep the prose, drop the keys.</summary>
    public static string CleanDetail(string detail)
    {
        var keep = detail.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0 && !p.StartsWith("groupSize=") && !p.StartsWith("idScope="));
        return string.Join("; ", keep);
    }

    public static int GroupSizeOf(string detail)
    {
        int i = detail.IndexOf("groupSize=", StringComparison.Ordinal);
        if (i < 0) return 0;
        var s = detail[(i + 10)..];
        int e = 0; while (e < s.Length && char.IsDigit(s[e])) e++;
        return int.TryParse(s[..e], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
