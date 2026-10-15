using System.Diagnostics;
using HoloDb;
using OrdersDemo;

namespace Showroom.Services;

public sealed record LoadStep(string Phase, double Fraction, int RowsDone);

/// <summary>What building the database cost, all measured in this tab. GenerateMs/BulkLoadMs/PlaneMs are compute only;
/// TotalMs is the wall clock the visitor waited (it includes the yields that let the progress bar repaint).</summary>
public sealed record LoadReport(int Rows, double GenerateMs, double BulkLoadMs, double PlaneMs, double TotalMs,
    long HeapBytes, long CommittedBytes, long PlaneBytes, long PlanePendingBytes, int JointBits);

/// <summary>One executed statement: the SQL that ran, the rows it returned, the ms it took (execute + read the result),
/// and the route HoloDb reported for it (Database.RouteTrace).</summary>
public sealed record StmtResult(DashQuery Stmt, string[] Columns, object[][] Rows, double Ms, string Route, string? Declined, string? Error);

public sealed record RunReport(IReadOnlyList<StmtResult> Results, double Ms);
public sealed record CompareRow(string Title, double PlaneMs, double EngineMs);
public sealed record CompareReport(int Reps, IReadOnlyList<CompareRow> Rows, bool SameAnswers);

/// <summary>Owns the in-browser HoloDb database and runs the statements DashboardSql writes. No SQL text lives here.</summary>
public sealed class DashboardEngine
{
    Database? _db;
    public int Rows { get; private set; }
    public bool Ready => _db is not null;

    public async Task<LoadReport> LoadAsync(int rows, Func<LoadStep, Task> progress, CancellationToken ct)
    {
        _db = null; Rows = 0;
        GC.Collect();                                              // let go of the previous table before the next one is built
        long start = Stopwatch.GetTimestamp();
        double gen = 0, bulk = 0;
        var db = Database.Open();
        db.Execute(DashboardSql.Schema);
        int customers = OrdersGenerator.Customers(rows);
        for (long first = 1; first <= rows; first += DashboardCode.ChunkRows)
        {
            ct.ThrowIfCancellationRequested();
            int n = (int)Math.Min(DashboardCode.ChunkRows, rows - first + 1);
            long t0 = Stopwatch.GetTimestamp();
            var chunk = OrdersGenerator.Fill(first, n, customers);
            long t1 = Stopwatch.GetTimestamp();
            db.BulkLoad(OrdersGenerator.Table, OrdersGenerator.Columns, chunk.AsArrays(), chunk.Count);
            long t2 = Stopwatch.GetTimestamp();
            gen += Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
            bulk += Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
            int done = (int)(first + n - 1);
            await progress(new LoadStep("Generating rows and loading the columns", 0.85 * done / rows, done));
        }
        await progress(new LoadStep("Building the aggregate plane (one pass over the columns)", 0.85, rows));
        long p0 = Stopwatch.GetTimestamp();
        db.Execute(DashboardSql.PlaneIndex);
        db.PleromaMode = PleromaMode.Always;
        double plane = Stopwatch.GetElapsedTime(p0).TotalMilliseconds;
        double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        var stats = db.PleromaStats(OrdersGenerator.Table);
        long heap = GC.GetTotalMemory(true);                       // after the clock stopped: a full collection is not part of the wait
        long committed = GC.GetGCMemoryInfo().TotalCommittedBytes;
        _db = db; Rows = rows;
        await progress(new LoadStep("Ready", 1, rows));
        return new LoadReport(rows, gen, bulk, plane, total, heap, committed, stats.PlaneBytes, stats.PendingBytes, stats.JointBits);
    }

    public RunReport Run(OrdersFilter f, Stage stage)
    {
        long t0 = Stopwatch.GetTimestamp();
        var list = DashboardSql.Plan(f).Where(s => s.Stage == stage).Select(Exec).ToList();
        return new RunReport(list, Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    /// <summary>The same summary statements with the plane on and then off (the engine's own scan), median of a few runs.</summary>
    public CompareReport Compare(OrdersFilter f)
    {
        int reps = Rows <= 1_000_000 ? 3 : 1;
        var rows = new List<CompareRow>();
        bool same = true;
        foreach (var s in DashboardSql.Plan(f).Where(x => x.Stage == Stage.Summary))
        {
            double a = Median(s, PleromaMode.Always, reps, out var ra);
            double b = Median(s, PleromaMode.Off, reps, out var rb);
            same &= Canon(ra) == Canon(rb);
            rows.Add(new CompareRow(s.Title, a, b));
        }
        _db!.PleromaMode = PleromaMode.Always;
        return new CompareReport(reps, rows, same);
    }

    double Median(DashQuery s, PleromaMode mode, int reps, out StmtResult last)
    {
        _db!.PleromaMode = mode;
        var ms = new List<double>();
        last = Exec(s);
        ms.Add(last.Ms);
        for (int i = 1; i < reps; i++) ms.Add(Exec(s).Ms);
        ms.Sort();
        return ms[ms.Count / 2];
    }

    static string Canon(StmtResult r) =>
        string.Join("|", r.Rows.Select(row => string.Join(",", row)).OrderBy(x => x, StringComparer.Ordinal));

    StmtResult Exec(DashQuery s)
    {
        var trace = Database.RouteTrace = new List<string>();
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            var r = _db!.Execute(s.Sql);
            var cols = r.Columns.ToArray();
            var rows = new object[r.RowCount][];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new object[cols.Length];
                for (int j = 0; j < cols.Length; j++) rows[i][j] = r.GetValue(i, j);
            }
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            string route = trace.LastOrDefault(x => x.StartsWith("route:")) is { } rt ? rt[6..].Split('#')[0] : "";
            var declined = trace.Where(x => x.StartsWith("decline:")).Select(x => x[8..]).ToList();
            return new StmtResult(s, cols, rows, ms, route, declined.Count == 0 ? null : string.Join("; ", declined), null);
        }
        catch (Exception e)
        {
            return new StmtResult(s, [], [], Stopwatch.GetElapsedTime(t0).TotalMilliseconds, "", null, e.Message);
        }
        finally { Database.RouteTrace = null; }
    }
}
