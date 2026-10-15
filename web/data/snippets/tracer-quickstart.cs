using System.Numerics;
using Tracer;
using Tracer.Models;

var world = new PathfindingWorld();

// A 6x4 room with a wall down the middle and a doorway in it.
// Two triangles per walkable cell; vertices sit on the integer lattice.
var vertices = new List<Vector2>();
var triangles = new List<(int A, int B, int C)>();
var lattice = new Dictionary<(int, int), int>();
int V(int x, int y)
{
    if (!lattice.TryGetValue((x, y), out var i)) { i = vertices.Count; lattice[(x, y)] = i; vertices.Add(new(x, y)); }
    return i;
}
for (int y = 0; y < 4; y++)
    for (int x = 0; x < 6; x++)
    {
        bool wall = x == 3 && y != 1;           // the doorway is the cell (3, 1)
        if (wall) continue;
        triangles.Add((V(x, y), V(x + 1, y), V(x + 1, y + 1)));
        triangles.Add((V(x, y), V(x + 1, y + 1), V(x, y + 1)));
    }

var mesh = await world.CreateNavMeshBakerService().BakeAsync(vertices, triangles);
Console.WriteLine($"triangles: {mesh.TriangleCount}");

// One smoothed path.
var path = await world.CreateSingleAgentService()
    .QueryPathAsync(mesh, new Vector2(0.5f, 3.5f), new Vector2(5.5f, 3.5f));
Console.WriteLine($"single path: {path.Count} waypoints, ends at {path[^1]}");

// A swarm sharing one goal is solved once.
var goal = new Vector2(5.5f, 0.5f);
var agents = Enumerable.Range(1, 3)
    .Select(i => new AgentDto(Id: i, Position: new Vector2(0.5f, i - 0.5f), Goal: goal, GoalChanged: true))
    .ToList();
var tick = await world.CreateMultiAgentService().TickAsync(agents, mesh, tickNumber: 0);
Console.WriteLine($"swarm: {tick.Paths.Count(p => p.PathFound)} of {agents.Count} agents have a path");

// Close the doorway at runtime. No re-bake.
var blocked = await world.CreateDynamicObstacleService()
    .UpdateAsync(mesh, new[] { new NavMeshBlockedZone(Center: new Vector2(3.5f, 1.5f), Radius: 0.6f) });
var after = await world.CreateSingleAgentService("after")
    .QueryPathAsync(blocked, new Vector2(0.5f, 3.5f), new Vector2(5.5f, 3.5f));
Console.WriteLine($"doorway closed: path found = {after.Count > 0}");
