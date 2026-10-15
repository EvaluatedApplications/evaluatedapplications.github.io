// Generates web/data/diagrams/holovoxel-lod.json by running the published HoloVoxel package.
//   dotnet run -c Release -- ../../data/diagrams/holovoxel-lod.json [seed] [firstChunkX] [chunkZ]
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HoloVoxel.Core;

string outPath = args.Length > 0 ? args[0] : "holovoxel-lod.json";
int seed = args.Length > 1 ? int.Parse(args[1]) : 2024;
int firstCx = args.Length > 2 ? int.Parse(args[2]) : 46;   // a strip with ocean, grassland and snow
int cz = args.Length > 3 ? int.Parse(args[3]) : 22;
var version = typeof(HoloWorld).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
int plus = version.IndexOf('+'); if (plus > 0) version = version[..plus];

const int Chunks = 9, Size = 16;
var gen = new WorldGen(seed) { NoWalls = true };
var world = new HoloWorld(gen, seed: seed);
world.Prewarm();
int full = world.Comps;

// ---- the three levels of detail this scene uses: the component counts come from Lod.Components ----
// distance in world units from the viewer to the middle of a chunk
double[] bandDistance = { 0, 64, 128 };
int[] stepFor = { 1, 2, 4 };
int[] compsFor = bandDistance.Select(dist => Lod.Components((int)dist, full)).ToArray();
// each chunk, by whole-chunk distance from the viewer, picks the level whose band covers it
var distanceTable = new List<int[]>();   // [chunks from the viewer, world units, components Lod.Components returns, band]
for (int k = 0; k < Chunks; k++)
{
    int dist = k * Size;
    int level = dist <= 32 ? 0 : dist <= 96 ? 1 : 2;   // the bands recorded in the output
    distanceTable.Add(new[] { k, dist, Lod.Components(dist, full), level });
}

// ---- colours: every distinct decoded colour, rounded to two decimals, indexed ----
var palette = new List<string>(); var paletteIndex = new Dictionary<string, int>();
int Col(VoxelColor v)
{
    string key = $"{(int)Math.Round(v.R * 100):D2}{(int)Math.Round(v.G * 100):D2}{(int)Math.Round(v.B * 100):D2}";
    string hex = $"{(int)Math.Round(Math.Clamp(v.R, 0, 1) * 255):x2}{(int)Math.Round(Math.Clamp(v.G, 0, 1) * 255):x2}{(int)Math.Round(Math.Clamp(v.B, 0, 1) * 255):x2}";
    if (!paletteIndex.TryGetValue(key, out int i)) { i = palette.Count; palette.Add(hex); paletteIndex[key] = i; }
    return i;
}

const int HeightBase = 40;
string Enc(int v, int width) { var sb = new StringBuilder(); for (int w = width - 1; w >= 0; w--) { int digit = (v >> (6 * w)) & 63; sb.Append((char)(48 + digit)); } return sb.ToString(); }

var chunkData = new List<object>();
int totalCells = 0;
for (int i = 0; i < Chunks; i++)
{
    int cx = firstCx + i;
    var levels = new List<object>();
    for (int lv = 0; lv < 3; lv++)
    {
        int step = stepFor[lv], comps = compsFor[lv];
        var chunk = new Chunk(cx, cz, world, step: step, comps: lv == 0 ? full : comps);
        var field = chunk.DecodeField(step, lv == 0 ? full : comps);
        int n = Size / step;
        var cells = new VoxelColor?[n * n];
        foreach (var v in field) { int lx = (v.X - cx * Size) / step, lz = (v.Z - cz * Size) / step; if (lx >= 0 && lx < n && lz >= 0 && lz < n) cells[lz * n + lx] = v; }
        var heights = new StringBuilder(); var colours = new StringBuilder();
        for (int c = 0; c < cells.Length; c++)
        {
            var v = cells[c] ?? throw new Exception($"missing cell {c} in chunk {cx} level {lv}");
            heights.Append((char)(HeightBase + v.Y)); colours.Append(Enc(Col(v), 2));
        }
        totalCells += cells.Length;
        levels.Add(new { step, n, heights = heights.ToString(), colours = colours.ToString() });
    }
    chunkData.Add(new { cx, levels });
}

// ---- the ladder: accuracy, stored size and decode cost against the component budget (chunk 0, full build) ----
var probe = new Chunk(firstCx, cz, world);
int[] budgets = { 24, 48, 98, 196, 392, 784, 1568, 3136, 6272, full };
var ladder = new List<object>();
foreach (int c in budgets)
{
    double acc = probe.Accuracy(c);
    var (_, bytes) = world.CapacityProbe(200, 8, c, 1);
    for (int w = 0; w < 3; w++) probe.DecodeField(1, c);
    var times = new List<double>();
    for (int r = 0; r < 15; r++) { var sw = Stopwatch.StartNew(); probe.DecodeField(1, c); times.Add(sw.Elapsed.TotalMilliseconds); }
    times.Sort();
    ladder.Add(new { components = c, accuracy = Math.Round(acc, 1), storedBytes = bytes, decodeMs = Math.Round(times[times.Count / 2], 2) });
    Console.WriteLine($"comps {c,5}: accuracy {acc,5:F1}%  bytes {bytes,7}  decode {times[times.Count / 2],6:F2} ms");
}

Console.WriteLine($"palette {palette.Count} colours, {totalCells} cells; comps for levels: {string.Join(",", compsFor)}");
foreach (var row in distanceTable) Console.WriteLine(string.Join(" ", row));
var doc = new
{
    schema = "ea-diagram/1", kind = "holovoxel-lod", generator = "web/generators/holovoxel-lod",
    package = new { id = "EvaluatedApplications.HoloVoxel", version },
    world = new { seed, chunkSize = Size, firstChunkX = firstCx, chunkZ = cz, chunks = Chunks, fullComponents = full, heightBase = HeightBase, seaLevel = WorldGen.SeaLevel },
    bands = new object[] { new { level = 0, maxUnits = 32 }, new { level = 1, maxUnits = 96 }, new { level = 2, maxUnits = (int?)null } },
    levels = Enumerable.Range(0, 3).Select(lv => new { step = stepFor[lv], components = lv == 0 ? full : compsFor[lv], probeDistance = bandDistance[lv] }).ToArray(),
    distances = distanceTable,
    palette,
    chunks = chunkData,
    ladder,
};
File.WriteAllText(outPath, JsonSerializer.Serialize(doc), new UTF8Encoding(false));
Console.WriteLine($"HoloVoxel {version}: wrote {outPath} ({new FileInfo(outPath).Length} bytes)");