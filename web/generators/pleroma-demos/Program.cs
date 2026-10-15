using System.Reflection;
using System.Text.Json;
using Phasor;

string superOut = args.Length > 0 ? args[0] : "pleroma-superposition.json";
string capOut = args.Length > 1 ? args[1] : "pleroma-capacity.json";

var version = typeof(Pleroma).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

// ---------------------------------------------------------------- superposition (K = 4, 3 flag bits, 7 ticks)
const int K = 4, FB = 3, T = 7;
var shape = new HologramShape { KeyCapacity = K, FlagBits = FB, TimePeriod = T, JointFaces = HologramFaces.Flags | HologramFaces.Time };
var items = new (long Key, uint Flags, int Time, long Count)[] { (10, 0b001, 0, 3), (20, 0b010, 2, 2), (30, 0b100, 4, 1) };
var store = new Pleroma(shape);
foreach (var it in items) store.Add(new HologramItem(Key: (ulong)it.Key, Flags: it.Flags, Time: it.Time), it.Count);
long[] flagsBefore = store.Flags(), timesBefore = store.Times();
long[] jointBefore = store.JointCounts(HologramFaces.Flags | HologramFaces.Time);

var kernelElements = new[] { (mask: 0u, shift: 0, weight: 1L), (mask: 0b001u, shift: 1, weight: 2L) };
var kernel = new PleromaKernel(store.Shape);
kernel.Add(weight: 1L);
kernel.Add(flagMask: 0b001, timeShift: 1, weight: 2L);
store.Apply(kernel);
long[] flagsAfter = store.Flags(), timesAfter = store.Times();
long[] jointAfter = store.JointCounts(HologramFaces.Flags | HologramFaces.Time);

// The per-item mapping shown in the replay is derived here and checked against the package's own joint counts.
var expected = new long[(1 << FB) * T];
var mapped = new List<object>();
foreach (var it in items)
    foreach (var e in kernelElements)
    {
        uint f = it.Flags ^ e.mask; int t = (it.Time + e.shift) % T; long c = it.Count * e.weight;
        expected[f * T + t] += c;
        mapped.Add(new { key = it.Key, from = new { flags = it.Flags, time = it.Time, count = it.Count }, weight = e.weight, to = new { flags = f, time = t, count = c } });
    }
if (!expected.SequenceEqual(jointAfter)) throw new Exception("derived item mapping disagrees with the package's joint counts");

var sup = new
{
    schema = "ea-diagram/1", kind = "pleroma-superposition", generator = "web/generators/pleroma-demos",
    package = new { id = "EvaluatedApplications.Phasor", version },
    shape = new { keyCapacity = K, flagBits = FB, timePeriod = T, harmonicsPerKeyLine = new Pleroma(new HologramShape { KeyCapacity = K }).CellCount, flagWords = 1 << FB, jointCells = (1 << FB) * T,
                  cellCount = store.CellCount, components = store.Components },
    items = items.Select(i => new { key = i.Key, flags = i.Flags, time = i.Time, count = i.Count }).ToArray(),
    kernel = kernelElements.Select(e => new { flagMask = e.mask, timeShift = e.shift, weight = e.weight }).ToArray(),
    before = new { total = items.Sum(i => i.Count), flags = flagsBefore, times = timesBefore, joint = jointBefore },
    after = new { total = flagsAfter.Sum(), flags = flagsAfter, times = timesAfter, joint = jointAfter },
    mapping = mapped,
};
File.WriteAllText(superOut, JsonSerializer.Serialize(sup), new System.Text.UTF8Encoding(false));

// ---------------------------------------------------------------- capacity: TryKeys with 0 to 2K+1 distinct keys
var rows = new List<object>();
foreach (int cap in new[] { 4 })
    for (int n = 0; n <= 2 * cap + 1; n++)
    {
        var s = new Pleroma(new HologramShape { KeyCapacity = cap });
        for (int i = 0; i < n; i++) s.Add(new HologramItem(Key: (ulong)(1000 + 37 * i)), 1 + i % 3);
        bool ok = s.TryKeys(out var keys);
        bool exact = ok && keys.Count == n;
        rows.Add(new { capacity = cap, keys = n, decoded = ok, exact, returned = ok ? keys.Count : 0 });
    }
var capShape = new HologramShape { KeyCapacity = 4 };
var capStore = new Pleroma(capShape);
var capDoc = new
{
    schema = "ea-diagram/1", kind = "pleroma-capacity", generator = "web/generators/pleroma-demos",
    package = new { id = "EvaluatedApplications.Phasor", version },
    capacity = 4, harmonicsPerKeyLine = capStore.CellCount,
    rows,
};
File.WriteAllText(capOut, JsonSerializer.Serialize(capDoc), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Phasor {version}: superposition total {sup.before.total} -> {sup.after.total}; capacity rows {rows.Count}");