// Generates the data behind the Tracer signature diagram by running the published
// EvaluatedApplications.Tracer package. Nothing here is hand-drawn: the level is an ASCII map,
// the navmesh is baked by Tracer, every path is Tracer output, the tile lane is GridTactics output.
//
//   dotnet run -c Release -- ../../data/diagrams/tracer-field.json
//
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Tracer;
using Tracer.Helpers;
using Tracer.Models;
using Tracer.Services;

string outPath = args.Length > 0 ? args[0] : "tracer-field.json";

const int H = 16, W = 32;
var cells = new char[H][];
for (int y = 0; y < H; y++)
{
    cells[y] = new char[W];
    for (int x = 0; x < W; x++)
        cells[y][x] = (x == 0 || y == 0 || x == W - 1 || y == H - 1) ? '#' : '.';
}
// Wall A (x=12) has doors at rows 4-5 and 10-11; wall B (x=21) has doors at rows 2-3 and 12-13.
for (int y = 1; y < H - 1; y++)
{
    if (y is not (4 or 5 or 10 or 11)) cells[y][12] = '#';
    if (y is not (2 or 3 or 12 or 13)) cells[y][21] = '#';
}
// A pillar block in the middle room.
for (int y = 7; y <= 9; y++) for (int x = 16; x <= 17; x++) cells[y][x] = '#';
string[] map = cells.Select(r => new string(r)).ToArray();
bool Wall(int x, int y) => x < 0 || y < 0 || x >= W || y >= H || map[y][x] == '#';

// ---- bake the navmesh: 2 triangles per walkable cell, shared lattice vertices ----
var vIndex = new Dictionary<(int, int), int>();
var vertices = new List<Vector2>();
int V(int x, int y)
{
    if (!vIndex.TryGetValue((x, y), out int i)) { i = vertices.Count; vIndex[(x, y)] = i; vertices.Add(new Vector2(x, y)); }
    return i;
}
var triangles = new List<(int A, int B, int C)>();
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
        if (!Wall(x, y))
        {
            int a = V(x, y), b = V(x + 1, y), c = V(x + 1, y + 1), d = V(x, y + 1);
            triangles.Add((a, b, c));
            triangles.Add((a, c, d));
        }

var stateDir = Path.Combine(Path.GetTempPath(), "tracer-field-" + Guid.NewGuid().ToString("N"));
var world = new PathfindingWorld(licenseKey: null, tunerStatePath: stateDir);
var baker = world.CreateNavMeshBakerService();
var mesh = await baker.BakeAsync(vertices, triangles);

// ---- the swarm: 14 agents in the left room, one shared goal in the right room ----
var goal = new Vector2(28.5f, 8.5f);
var starts = new (float X, float Y)[]
{
    (1.5f, 1.5f), (3.5f, 2.5f), (5.5f, 1.5f), (7.5f, 3.5f), (2.5f, 5.5f), (6.5f, 5.5f), (9.5f, 6.5f),
    (1.5f, 8.5f), (4.5f, 9.5f), (7.5f, 8.5f), (2.5f, 11.5f), (5.5f, 12.5f), (8.5f, 13.5f), (10.5f, 8.5f),
};
// One agent keeps a different goal so the data also shows goal-sharing next to a solo solve.
var agents = starts.Select((s, i) => new AgentDto(
    Id: i + 1,
    Position: new Vector2(s.X, s.Y),
    Goal: i == 13 ? new Vector2(16.5f, 13.5f) : goal,
    GoalChanged: true)).ToList();

var multi = world.CreateMultiAgentService();
var tick1 = await multi.TickAsync(agents, mesh, tickNumber: 0);

// ---- a dynamic obstacle isolates a doorway in the first wall ----
var obstacles = world.CreateDynamicObstacleService();
var zone = new NavMeshBlockedZone(Center: new Vector2(12.5f, 5.0f), Radius: 1.3f);
var blockedMesh = await obstacles.UpdateAsync(mesh, new[] { zone });
var multi2 = world.CreateMultiAgentService("blocked");
var tick2 = await multi2.TickAsync(agents, blockedMesh, tickNumber: 1);

// ---- a single smoothed query for the first agent, flat mesh ----
var single = world.CreateSingleAgentService();
var singlePath = await single.QueryPathAsync(mesh, agents[0].Position, goal);
var batch = await single.QueryPathBatchAsync(mesh, agents.Select(a => (a.Position, a.Goal)).ToArray());

// ---- tile lane: GridTactics over the same map, no bake ----
bool Passable(int x, int y) => !Wall(x, y);
bool Blocks(int x, int y) => Wall(x, y);
int ox = 4, oy = 8, steps = 7;
var reach = GridTactics.Reachable(ox, oy, steps, Passable);
var visible = new List<int[]>();
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
        if (!Wall(x, y) && Math.Max(Math.Abs(x - ox), Math.Abs(y - oy)) <= 12 && GridTactics.LineOfSight(ox, oy, x, y, Blocks))
            visible.Add(new[] { x, y });
var cover = reach.Keys.Where(k => GridTactics.InCover(k.Item1, k.Item2, Blocks)).Select(k => new[] { k.Item1, k.Item2 }).ToList();

double R(float v) => Math.Round(v, 2);
double[][] Pts(IReadOnlyList<Vector2> wps) => wps.Select(p => new[] { R(p.X), R(p.Y) }).ToArray();
object Scenario(string key, IEnumerable<AgentDto> ag, TickResult res) => new
{
    key,
    pressure = Math.Round(res.Pressure, 3),
    agents = ag.Select(a =>
    {
        var r = res.Paths.First(p => p.AgentId == a.Id);
        return new
        {
            id = a.Id,
            from = new[] { R(a.Position.X), R(a.Position.Y) },
            goal = new[] { R(a.Goal.X), R(a.Goal.Y) },
            found = r.PathFound,
            path = Pts(r.Waypoints),
        };
    }).ToArray(),
};

var pkgVersion = typeof(PathfindingWorld).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                 ?? typeof(PathfindingWorld).Assembly.GetName().Version?.ToString() ?? "unknown";
int plus = pkgVersion.IndexOf('+'); if (plus > 0) pkgVersion = pkgVersion[..plus];

var doc = new
{
    schema = "ea-diagram/1",
    kind = "tracer-field",
    generator = "web/generators/tracer-field",
    package = new { id = "EvaluatedApplications.Tracer", version = pkgVersion },
    grid = new { width = W, height = H, rows = map },
    mesh = new { triangles = mesh.TriangleCount, trianglesAfterObstacle = blockedMesh.TriangleCount },
    zone = new { center = new[] { R(zone.Center.X), R(zone.Center.Y) }, radius = R(zone.Radius) },
    swarm = Scenario("swarm", agents, tick1),
    blocked = Scenario("blocked", agents, tick2),
    singles = agents.Select((a, i) => new { id = a.Id, found = batch[i].Count > 0, path = Pts(batch[i]) }).ToArray(),
    tile = new
    {
        origin = new[] { ox, oy },
        steps,
        reach = reach.OrderBy(k => k.Value).ThenBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2)
                     .Select(k => new[] { k.Key.Item1, k.Key.Item2, k.Value }).ToArray(),
        visible,
        cover,
    },
};
var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
File.WriteAllText(outPath, json, new System.Text.UTF8Encoding(false));
Console.WriteLine($"Tracer {pkgVersion}: {mesh.TriangleCount} triangles, wrote {outPath} ({json.Length} bytes)");
Console.WriteLine($"swarm found {tick1.Paths.Count(p => p.PathFound)}/{agents.Count}, blocked found {tick2.Paths.Count(p => p.PathFound)}/{agents.Count}");
try { Directory.Delete(stateDir, true); } catch { }
