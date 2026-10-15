// Generates web/data/diagrams/evalapp-gate.json by running the published EvalApp package.
// A batch of 24 jobs is queued at once. Each job downloads through a gated network resource. For several gate bounds
// the run records when every job was queued, admitted and finished.
//   dotnet run -c Release -- ../../data/diagrams/evalapp-gate.json
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EvalApp;

string outPath = args.Length > 0 ? args[0] : "evalapp-gate.json";
var version = typeof(Eval).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

const int Jobs = 24;
const double BaseMs = 40;     // one simulated download
const int Knee = 6;          // the simulated service slows down once more than this many are in flight

var sw = Stopwatch.StartNew();
int inFlight = 0;

double Delay(int concurrent)
{
    if (concurrent <= Knee) return BaseMs;
    double over = (concurrent - Knee) / (double)Knee;
    return BaseMs * (1 + over * over);
}

(ICompiledPipeline<Batch> Pipe, Func<int> Peak, Action ResetPeak) Build(string name, Func<IAppBuilder, IAppBuilder> setup)
{
    int peak = 0;
    var app = setup(Eval.App(name));
    ICompiledPipeline<Batch> pipeline;
    app.DefineDomain("Fetch")
        .DefineTask<Batch>("FetchAll")
            .ForEach(
                select: b => b.Jobs,
                merge: (b, js) => b with { Jobs = js.ToList() },
                collectionName: "Jobs",
                parallelism: Tunable.Between(Jobs, Jobs, Jobs),
                configure: sub => sub
                    .AddStep("Queue", (Job j) => j with { Queued = sw.Elapsed.TotalMilliseconds })
                    .Gate(ResourceKind.Network, onWaiting: null, configure: g => g
                        .AddStep("Download", async (Job j, CancellationToken ct) =>
                        {
                            int now = Interlocked.Increment(ref inFlight);
                            int p; while ((p = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, p) != p) { }
                            double start = sw.Elapsed.TotalMilliseconds;
                            try { await Task.Delay(TimeSpan.FromMilliseconds(Delay(now)), ct); }
                            finally { Interlocked.Decrement(ref inFlight); }
                            return j with { Started = start, Finished = sw.Elapsed.TotalMilliseconds, InFlight = now };
                        })))
        .Run(out pipeline)
        .Build();
    return (pipeline, () => peak, () => peak = 0);
}

var fresh = Enumerable.Range(1, Jobs).Select(i => new Job(i)).ToList();
int R(double v) => (int)Math.Round(v);

// ---- fixed gate bounds ----
var bounds = new[] { 1, 2, 4, 6, 8, 12, 24 };
var runs = new List<object>();
foreach (int b in bounds)
{
    var (pipe, peak, reset) = Build("Fixed" + b, a => a.WithResource(ResourceKind.Network, Tunable.FixedAt(b)));
    await pipe.RunAsync(new Batch(fresh));   // warm-up: JIT and thread pool
    reset();
    double t0 = sw.Elapsed.TotalMilliseconds;
    var res = await pipe.RunAsync(new Batch(fresh));
    double t1 = sw.Elapsed.TotalMilliseconds;
    var data = res.GetData().Jobs;
    runs.Add(new
    {
        bound = b,
        makespanMs = R(t1 - t0),
        peakInFlight = peak(),
        // [id, queued, started, finished, downloads in flight when it started], milliseconds from the start of the run
        jobs = data.Select(j => new[] { j.Id, R(j.Queued - t0), R(j.Started - t0), R(j.Finished - t0), j.InFlight }).ToList(),
    });
    Console.WriteLine($"bound {b,2}: makespan {t1 - t0,7:F1} ms, peak in flight {peak(),2}");
}

var doc = new
{
    schema = "ea-diagram/1", kind = "evalapp-gate", generator = "web/generators/evalapp-gate",
    package = new { id = "EvaluatedApplications.EvalApp", version },
    workload = new { jobs = Jobs, baseMs = BaseMs, knee = Knee },
    fixedRuns = runs,
};
File.WriteAllText(outPath, JsonSerializer.Serialize(doc), new System.Text.UTF8Encoding(false));
Console.WriteLine($"EvalApp {version}: wrote {outPath}");

public sealed record Job(int Id, double Queued = 0, double Started = 0, double Finished = 0, int InFlight = 0);
public sealed record Batch(List<Job> Jobs);
