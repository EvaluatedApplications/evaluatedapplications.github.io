using OrdersDemo;

namespace Showroom.Services;

public sealed record Slice(string Key, long Count, long Sum)
{
    public long Average => Count == 0 ? 0 : (long)Math.Round((double)Sum / Count);
}

/// <summary>Everything the dashboard page shows: the engine, the brushing, the latest results and the visitor's own timings.
/// The page only renders this; components call the methods, which re-run the plan and ask the page to repaint.</summary>
public sealed class DashboardSession
{
    public static readonly int[] Sizes = { 100_000, 500_000, 1_000_000, 2_000_000, 5_000_000 };

    public readonly DashboardEngine Engine = new();
    public readonly OrdersFilter Filter = new();
    public LatencyLog Latency { get; private set; } = new();
    public Func<Task> Repaint = () => Task.CompletedTask;

    public int Size = 1_000_000;
    public LoadStep? Step;
    public LoadReport? Load;
    public string? Error;
    public bool Building, DetailPending, Comparing;
    public bool AsTable;
    public string Metric = "revenue";                 // revenue | orders | average
    public IReadOnlyList<StmtResult> Summary = [], Detail = [];
    public double SummaryMs, DetailMs, FirstPaintMs;
    public CompareReport? Compare;
    public string Announce = "";
    int _seq;
    CancellationTokenSource? _cts;

    public bool Ready => Load is not null && !Building;

    public async Task BuildAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Building = true; Error = null; Load = null; Compare = null; Summary = Detail = [];
        Filter.Clear(); Latency = new LatencyLog();
        try
        {
            Step = new LoadStep("Starting", 0, 0);
            await Repaint();
            Load = await Engine.LoadAsync(Size, async st => { Step = st; await Repaint(); }, _cts.Token);
            Building = false;
            await RefreshAsync(first: true);
        }
        catch (OperationCanceledException) { }
        catch (OutOfMemoryException)
        {
            Error = $"The browser ran out of memory building {DashboardFormat.N(Size)} rows. Pick a smaller size.";
        }
        catch (Exception e) { Error = e.Message; }
        finally { Building = false; }
        await Repaint();
    }

    public async Task RefreshAsync(bool first = false)
    {
        int mine = ++_seq;
        DetailPending = true; Compare = null;
        var s = Engine.Run(Filter, Stage.Summary);
        Summary = s.Results; SummaryMs = s.Ms;
        await Repaint();                              // the plane-served panels paint before the scans start
        if (mine != _seq) return;
        var d = Engine.Run(Filter, Stage.Detail);
        Detail = d.Results; DetailMs = d.Ms; DetailPending = false;
        if (first) FirstPaintMs = SummaryMs + DetailMs; else Latency.Add(SummaryMs, DetailMs);
        var t = Totals;
        Announce = $"{DashboardFormat.N(t.Count)} orders, {DashboardFormat.Money(t.Sum)} revenue. Summary panels answered in {DashboardFormat.Ms(SummaryMs)}.";
        await Repaint();
    }

    public Task ToggleAsync(string dim, string key) { Filter.Toggle(dim, key); return RefreshAsync(); }
    public Task MonthAsync(int m) { Filter.ClickMonth(m); return RefreshAsync(); }
    public Task ClearAsync() { Filter.Clear(); return RefreshAsync(); }
    public Task ClearMonthsAsync() { Filter.SetMonths(-1, -1); return RefreshAsync(); }

    public Task YearAsync(int year)
    {
        if (Filter.MonthLo == year * 12 && Filter.MonthHi == year * 12 + 11) Filter.SetMonths(-1, -1);
        else Filter.SetMonths(year * 12, year * 12 + 11);
        return RefreshAsync();
    }

    public async Task CompareAsync()
    {
        Comparing = true; await Repaint();
        Compare = Engine.Compare(Filter);
        Comparing = false; await Repaint();
    }

    // ---- views over the latest results ----
    StmtResult? Find(IReadOnlyList<StmtResult> list, string id) => list.FirstOrDefault(r => r.Stmt.Id == id);

    public IReadOnlyList<StmtResult> AllResults => Summary.Concat(Detail).ToList();

    public (long Count, long Sum) Totals =>
        Find(Summary, "tiles") is { Rows.Length: > 0 } r ? (ToLong(r.Rows[0][0]), ToLong(r.Rows[0][1])) : (0, 0);

    /// <summary>Slices for a chart, merged with the full list of values so an emptied bucket shows as zero, not as missing.</summary>
    public IReadOnlyList<Slice> Slices(string id, IEnumerable<string> universe)
    {
        var got = (Find(Summary, id)?.Rows ?? []).ToDictionary(r => r[0].ToString() ?? "", r => new Slice(r[0].ToString() ?? "", ToLong(r[1]), ToLong(r[2])));
        return universe.Select(k => got.TryGetValue(k, out var s) ? s : new Slice(k, 0, 0)).ToList();
    }

    public StmtResult? Result(string id) => Find(Summary, id) ?? Find(Detail, id);

    public static long ToLong(object? o) => o switch
    {
        null => 0, long l => l, int i => i, Int128 h => (long)h, double d => (long)d, decimal m => (long)m, _ => Convert.ToInt64(o),
    };
}
