using System.Numerics;
using Tracer.Models;

namespace Doctrine.Battle;

// The arena catalogue of the Tracer battle section. Each arena is a handmade ASCII layout (the LEFT half; the right half is its mirror,
// so neither side owns the ground) that is turned into triangles and baked by Tracer's NavMeshBaker. The layout is only ever the
// SOURCE of the mesh and the place rules (cover, the gate); all movement, sight and path questions are asked of Tracer.
//
// Legend: '.' open ground, '#' wall (no triangles are emitted, so the baked mesh has a hole there), ',' cover (walkable, hits taken
// are reduced), 'G' gate cell (walkable until the gate falls).
// SIEGE ARENAS (ArenaSpec.Siege): the two with a wall between the armies (chokepoint, falling gate). The open field and the ridge are open.

/// <summary>One arena's source layout and rules. Cells are <see cref="Cell"/> arena units wide.</summary>
internal sealed class ArenaSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>The left half of the map, one string per row (row 0 is y = 0). The full map is the half plus its mirror.</summary>
    public required string[] Half { get; init; }
    public int Cell { get; init; } = 2;
    /// <summary>The tick at which the gate falls (0 = this arena has no gate). It waits until nobody stands in it.</summary>
    public int GateFallsAtTick { get; init; }
    /// <summary>A siege arena: a wall stands between the armies and the besieger carries the siege clock (<see cref="Arena.SiegeClockTicks"/>). Open fields have none.</summary>
    public bool Siege { get; init; }
}

/// <summary>A parsed arena: the cell grid, the geometry Tracer bakes, and the position rules.</summary>
internal sealed class Arena
{
    readonly char[,] _cells;     // [col, row]
    public ArenaSpec Spec { get; }
    public ArenaInfo Info { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int Cell => Spec.Cell;
    public float Width => Cols * Cell;
    public float Height => Rows * Cell;
    /// <summary>The circular zone handed to the dynamic-obstacle service when the gate falls (null when the arena has no gate).</summary>
    public NavMeshBlockedZone? GateZone { get; }

    readonly bool _legacyMesh;

    /// <summary>Where the front deployment line stands, in percent of the width from the arena's own edge (the back lines stand 10 percent deeper each).</summary>
    public const int FrontDepthPct = 30;

    /// <summary>
    /// The siege clock in ticks (0 for an open field), DERIVED from the arena: the time the slowest walking kind (Heavy) needs to cross the ground between the two front
    /// lines, ceil(gap / pace) with gap = width x (100 - 2 x <see cref="FrontDepthPct"/>) percent. In tenths of a unit so the figure is exact on every device:
    /// chokepoint (48 wide) 14 ticks, falling gate (56 wide) 16.
    /// </summary>
    public int SiegeClockTicks
    {
        get
        {
            if (!Spec.Siege) return 0;
            long gapTenths = (long)Width * (100 - 2 * FrontDepthPct) / 10;
            long paceTenths = (long)MathF.Round(UnitRules.Speed(Trait.Heavy) * 10f);
            return (int)((gapTenths + paceTenths - 1) / paceTenths);
        }
    }

    public Arena(ArenaSpec spec, bool legacyMesh = false)
    {
        Spec = spec; _legacyMesh = legacyMesh;
        int half = spec.Half[0].Length;
        Rows = spec.Half.Length; Cols = half * 2;
        _cells = new char[Cols, Rows];
        for (int r = 0; r < Rows; r++)
        {
            if (spec.Half[r].Length != half) throw new InvalidOperationException($"arena {spec.Id}: row {r} is {spec.Half[r].Length} wide, expected {half}");
            for (int c = 0; c < half; c++) { _cells[c, r] = spec.Half[r][c]; _cells[Cols - 1 - c, r] = spec.Half[r][c]; }
        }
        Info = new ArenaInfo(spec.Id, spec.Name, Cols * Cell, Rows * Cell, SiegeClockTicks);

        // The gate zone: a circle that covers every G cell (and a little of the ground next to them).
        int minC = int.MaxValue, maxC = -1, minR = int.MaxValue, maxR = -1;
        for (int r = 0; r < Rows; r++) for (int c = 0; c < Cols; c++)
            if (_cells[c, r] == 'G') { minC = Math.Min(minC, c); maxC = Math.Max(maxC, c); minR = Math.Min(minR, r); maxR = Math.Max(maxR, r); }
        if (maxC >= 0)
        {
            var centre = new Vector2((minC + maxC + 1) * Cell / 2f, (minR + maxR + 1) * Cell / 2f);
            float rx = (maxC - minC + 1) * Cell / 2f, ry = (maxR - minR + 1) * Cell / 2f;
            GateZone = new NavMeshBlockedZone(centre, MathF.Sqrt(rx * rx + ry * ry) + 0.3f);
        }
    }

    /// <summary>The whole map, one string per row (the left half plus its mirror).</summary>
    public IReadOnlyList<string> FullRows()
    {
        var rows = new List<string>(Rows);
        for (int r = 0; r < Rows; r++) { var chars = new char[Cols]; for (int c = 0; c < Cols; c++) chars[c] = _cells[c, r]; rows.Add(new string(chars)); }
        return rows;
    }

    public bool IsOpenCell(int c, int r) => c >= 0 && r >= 0 && c < Cols && r < Rows && _cells[c, r] != '#';
    int CellX(float x) => Math.Clamp((int)MathF.Floor(x / Cell), 0, Cols - 1);
    int CellY(float y) => Math.Clamp((int)MathF.Floor(y / Cell), 0, Rows - 1);
    public bool IsOpen(Vector2 p) => IsOpenCell(CellX(p.X), CellY(p.Y));
    public bool InCover(Vector2 p) => _cells[CellX(p.X), CellY(p.Y)] == ',';

    /// <summary>The nearest walkable ground to <paramref name="p"/>: the point itself if it is open, else the centre of the closest open cell.</summary>
    public Vector2 Snap(Vector2 p)
    {
        p = new Vector2(Math.Clamp(p.X, 0.05f, Width - 0.05f), Math.Clamp(p.Y, 0.05f, Height - 0.05f));
        int c0 = CellX(p.X), r0 = CellY(p.Y);
        if (IsOpenCell(c0, r0)) return p;
        float best = float.MaxValue; Vector2 bestP = p;
        for (int ring = 1; ring <= Math.Max(Cols, Rows); ring++)
        {
            for (int dr = -ring; dr <= ring; dr++)
                for (int dc = -ring; dc <= ring; dc++)
                {
                    if (Math.Max(Math.Abs(dc), Math.Abs(dr)) != ring) continue;
                    int c = c0 + dc, r = r0 + dr;
                    if (!IsOpenCell(c, r)) continue;
                    var centre = new Vector2((c + 0.5f) * Cell, (r + 0.5f) * Cell);
                    float d = Vector2.DistanceSquared(centre, p);
                    if (d < best) { best = d; bestP = centre; }
                }
            if (best < float.MaxValue) return bestP;
        }
        return p;
    }

    /// <summary>The triangles Tracer's baker turns into the navmesh: two per open cell on a shared lattice (so neighbours share edges).</summary>
    public (List<Vector2> Vertices, List<(int A, int B, int C)> Triangles) Geometry()
    {
        var index = new Dictionary<(int, int), int>();
        var vertices = new List<Vector2>();
        int V(int x, int y)
        {
            if (!index.TryGetValue((x, y), out int i)) { i = vertices.Count; index[(x, y)] = i; vertices.Add(new Vector2(x * Cell, y * Cell)); }
            return i;
        }
        var triangles = new List<(int, int, int)>();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_cells[c, r] != '#')
                {
                    int a = V(c, r), b = V(c + 1, r), cc = V(c + 1, r + 1), d = V(c, r + 1);
                    // The right half is split along the REFLECTION of the left half's diagonal, so the mesh is its own mirror image. With one diagonal
                    // everywhere the two armies' ground was not equal (different corridors and sight lines), and mirrored armies leaned to the
                    // attacker 43 to 8 on the ridge (Doctrine\todo\balance-harness-2026-10-19.md).
                    if (!_legacyMesh && c >= Cols / 2) { triangles.Add((a, b, d)); triangles.Add((b, cc, d)); }
                    else { triangles.Add((a, b, cc)); triangles.Add((a, cc, d)); }
                }
        return (vertices, triangles);
    }
}

/// <summary>The handmade catalogue. Ids are wire data (replays name them): never rename one.</summary>
internal static class ArenaCatalogue
{
    // 32 x 20 cells of 2 units = 64 x 40. Two rock pillars and two thickets of cover; the deployment bands stay clear.
    static readonly ArenaSpec OpenField = new()
    {
        Id = "open-field", Name = "Open Field",
        Half =
        [
            "................",
            "................",
            ".........,,.....",
            ".........,,.....",
            "...........#....",
            "................",
            "................",
            ".......##.......",
            ".......##.......",
            "................",
            "................",
            ".......##.......",
            ".......##.......",
            "................",
            "................",
            "...........#....",
            ".........,,.....",
            ".........,,.....",
            "................",
            "................",
        ],
    };

    // 24 x 20 cells = 48 x 40. A wall across the middle with ONE gap, eight units wide; cover on both approaches.
    static readonly ArenaSpec Chokepoint = new()
    {
        Id = "chokepoint", Name = "The Chokepoint", Siege = true,
        Half =
        [
            "...........#",
            "...........#",
            "...........#",
            "...........#",
            "...........#",
            "...........#",
            "........,,.#",
            "........,,.#",
            "............",
            "............",
            "............",
            "............",
            "........,,.#",
            "........,,.#",
            "...........#",
            "...........#",
            "...........#",
            "...........#",
            "...........#",
            "...........#",
        ],
    };

    // 40 x 16 cells = 80 x 32. Two ridges run along a road with a side opening each; open lanes above and below the ridges; cover on the road.
    static readonly ArenaSpec Ridge = new()
    {
        Id = "ridge", Name = "Ridge Road",
        Half =
        [
            "....................",
            "....................",
            "....................",
            ".....##########.####",
            ".....##########.####",
            "........,,..........",
            "....................",
            "....................",
            "....................",
            "....................",
            "........,,..........",
            ".....##########.####",
            ".....##########.####",
            "....................",
            "....................",
            "....................",
        ],
    };

    // 28 x 20 cells = 56 x 40. A thick wall with three doors; the middle door has a gate that falls at tick 14 (waiting until it is clear).
    static readonly ArenaSpec FallingGate = new()
    {
        Id = "falling-gate", Name = "The Falling Gate",
        GateFallsAtTick = 14, Siege = true,
        Half =
        [
            ".............#",
            ".............#",
            ".............#",
            ".............#",
            ".............#",
            "..........,,..",
            "..........,,..",
            ".............#",
            ".............G",
            ".............G",
            ".............G",
            ".............G",
            ".............#",
            "..........,,..",
            "..........,,..",
            ".............#",
            ".............#",
            ".............#",
            ".............#",
            ".............#",
        ],
    };

    public static IReadOnlyList<Arena> Build(bool legacyMesh = false)
    {
        var specs = new[] { OpenField, Chokepoint, Ridge, FallingGate };
        return specs.Select(s => new Arena(s, legacyMesh)).ToList();
    }
}
