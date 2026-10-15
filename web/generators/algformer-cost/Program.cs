// Generates web/data/diagrams/algformer-cost.json by running the published AlgFormer package.
// Both cores are built at the same shape (same parameter count) and driven with the same random tokens.
//   dotnet run -c Release -- ../../data/diagrams/algformer-cost.json
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PrismFormer;

string outPath = args.Length > 0 ? args[0] : "algformer-cost.json";
var version = typeof(AlgFormer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

const int V = 64, S = 8, L = 2, D = 32, MaxContext = 2048, StepsTimed = 16, Reps = 7;
int[] lengths = { 64, 128, 256, 512, 1024, 1984 };

var alg = new AlgFormer(vocab: V, shifts: S, layers: L, maxContext: MaxContext, dModel: D, frozenPrefix: 0);
var holo = new HoloFormer(vocab: V, shifts: S, layers: L, maxContext: MaxContext, dModel: D, frozenPrefix: 0);
var rnd = new Random(1);
int[] all = Enumerable.Range(0, MaxContext + 64).Select(_ => rnd.Next(V)).ToArray();

double Median(List<double> xs) { xs.Sort(); return xs.Count % 2 == 1 ? xs[xs.Count / 2] : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2; }

// one measurement of reading n tokens and then producing StepsTimed more, for either core
(double prefillMs, double stepUs) Measure(bool holographic, int n)
{
    var toks = all[..n];
    var sw = Stopwatch.StartNew();
    double prefill;
    double stepUs;
    if (holographic)
    {
        var c = holo.NewCache(); holo.Prime(c, toks); prefill = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); for (int i = 0; i < StepsTimed; i++) holo.Step(c, all[n + i]); stepUs = sw.Elapsed.TotalMilliseconds * 1000 / StepsTimed;
    }
    else
    {
        var c = alg.NewCache(); alg.Prime(c, toks); prefill = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); for (int i = 0; i < StepsTimed; i++) alg.Step(c, all[n + i]); stepUs = sw.Elapsed.TotalMilliseconds * 1000 / StepsTimed;
    }
    return (prefill, stepUs);
}

// bytes the primed cache keeps alive
long CacheBytes(bool holographic, int n)
{
    var toks = all[..n];
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long before = GC.GetTotalMemory(true);
    object cache;
    if (holographic) { var c = holo.NewCache(); holo.Prime(c, toks); cache = c; }
    else { var c = alg.NewCache(); alg.Prime(c, toks); cache = c; }
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long after = GC.GetTotalMemory(true);
    GC.KeepAlive(cache);
    return after - before;
}

// warm up every code path at every length so that tiered compilation has settled
foreach (int n in lengths) for (int w = 0; w < 2; w++) { Measure(false, n); Measure(true, n); }

var rows = new List<object>();
foreach (int n in lengths)
{
    var pa = new List<double>(); var sa = new List<double>(); var ph = new List<double>(); var sh = new List<double>();
    for (int r = 0; r < Reps; r++)
    {
        var a = Measure(false, n); pa.Add(a.prefillMs); sa.Add(a.stepUs);
        var h = Measure(true, n); ph.Add(h.prefillMs); sh.Add(h.stepUs);
    }
    long ba = CacheBytes(false, n), bh = CacheBytes(true, n);
    var row = new
    {
        tokens = n,
        prefillMs = new { softmax = Math.Round(Median(pa), 2), holographic = Math.Round(Median(ph), 2) },
        stepUs = new { softmax = Math.Round(Median(sa), 1), holographic = Math.Round(Median(sh), 1) },
        cacheKb = new { softmax = Math.Round(ba / 1024.0, 1), holographic = Math.Round(bh / 1024.0, 1) },
    };
    rows.Add(row);
    Console.WriteLine($"n={n,5}  prefill softmax {row.prefillMs.softmax,9:F2} ms  holo {row.prefillMs.holographic,8:F2} ms | step softmax {row.stepUs.softmax,8:F1} us  holo {row.stepUs.holographic,7:F1} us | cache softmax {row.cacheKb.softmax,9:F1} KB  holo {row.cacheKb.holographic,8:F1} KB");
}

// capacity numbers HoloShape derives for several widths
var capacity = new List<object>();
foreach (int dim in new[] { 32, 64, 128, 256, 512 })
{
    var dv = HoloShape.Derive(dim);
    capacity.Add(new
    {
        dim, shifts = dv.Shifts, context = dv.Context, layers = dv.Layers,
        bindRank = HoloShape.BindRank(dv.Shifts, dim), cleanCapacity = HoloShape.CleanCapacity(dv.Shifts, dim),
    });
}
Console.WriteLine("capacity: " + JsonSerializer.Serialize(capacity));

var doc = new
{
    schema = "ea-diagram/1", kind = "algformer-cost", generator = "web/generators/algformer-cost",
    package = new { id = "EvaluatedApplications.AlgFormer", version },
    shape = new { vocab = V, shifts = S, layers = L, dModel = D, maxContext = MaxContext, softmaxParams = alg.ParamCount, holographicParams = holo.ParamCount },
    method = new { reps = Reps, stepsTimed = StepsTimed, statistic = "median", threads = "one", machineCores = Environment.ProcessorCount },
    rows,
    capacity,
    cleanFrac = HoloShape.CleanFrac,
};
File.WriteAllText(outPath, JsonSerializer.Serialize(doc), new System.Text.UTF8Encoding(false));
Console.WriteLine($"AlgFormer {version}: wrote {outPath}");