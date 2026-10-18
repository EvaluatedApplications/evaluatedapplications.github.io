using Tracer.Abstractions;
using Tracer.Integration;
using Tracer.Models;
using Tracer.Services;

namespace Doctrine.Battle;

/// <summary>
/// What a page needs to draw an arena under a recording (the recording carries cohorts, never terrain): the map as text
/// (<c>.</c> open, <c>#</c> wall, <c>,</c> cover, <c>G</c> gate, one string per row) and the gate that falls.
/// UNITS: a cell is <see cref="CellSize"/> ARENA UNITS wide and tall, so the row string at index r, character c covers arena x in
/// [c * CellSize, (c + 1) * CellSize) and y in [r * CellSize, (r + 1) * CellSize); the same arena units the recording's blob positions use
/// once divided by <see cref="BattleReplay.Quantum"/> (positions are stored at 1/16 of a unit).
/// THE GATE: <c>G</c> cells are walkable ground until the gate falls and BLOCKED from then on. The fall is in the recording: frames from
/// the first one with <see cref="WorldFlag.GateFallen"/> set show the ground with the G cells closed; earlier frames show them open.
/// Gate numbers (<see cref="GateX"/>, <see cref="GateY"/>, <see cref="GateRadius"/>) are integers at 1/16 unit, like positions;
/// <see cref="GateFallsAtTick"/> is 0 for an arena without a gate.
/// </summary>
public sealed record ArenaLayout(string Id, int CellSize, IReadOnlyList<string> Cells, int GateX, int GateY, int GateRadius, int GateFallsAtTick);

/// <summary>
/// The battle section on Tracer (seam b). It is handed the Tracer extension's own <see cref="TracerContext"/> (the game holds it:
/// <c>DoctrineBuilder.New().WithTracer(world).WithBattle(ctx =&gt; new TracerBattleEngine(ctx.Tracer!))</c>) and creates every service through
/// that context's <c>PathfindingWorld</c>. An engagement service the context already carries is used as is.
/// Arenas are baked once per engine (the baker is the only awaited step of <see cref="StartAsync"/>); every battle gets fresh pathfinding
/// services, because the multi-agent service remembers agents by id between ticks and a second battle must not inherit the first's.
/// It is an <see cref="IArenaLayouts"/>: a host draws arenas through <see cref="DoctrineContext.LayoutOf"/>, without naming this type.
/// </summary>
public sealed class TracerBattleEngine : IBattleEngine, IArenaLayouts
{
    readonly TracerContext _tracer;
    readonly TracerBattleOptions _options;
    readonly IReadOnlyList<Arena> _arenas = ArenaCatalogue.Build();
    readonly INavMeshBakerService _baker;
    readonly IEngagementService _engagement;
    readonly IInfluenceMapService _influence;
    readonly Dictionary<string, Task<NavMeshHandle>> _baked = new();
    readonly IReadOnlyList<ArenaInfo> _info;

    public TracerBattleEngine(TracerContext tracer, TracerBattleOptions? options = null)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        _options = options ?? new TracerBattleOptions();
        _baker = tracer.World.CreateNavMeshBakerService("doctrine-baker");
        _engagement = tracer.EngagementService ?? tracer.World.CreateEngagementService("doctrine-engagement");
        _influence = tracer.World.CreateInfluenceMapService("doctrine-influence");
        _info = _arenas.Select(a => a.Info).ToList();
    }

    public IReadOnlyList<ArenaInfo> Arenas => _info;

    /// <summary>The drawable layout of an arena (null for an unknown id).</summary>
    public ArenaLayout? LayoutOf(string arenaId)
    {
        var a = _arenas.FirstOrDefault(x => x.Info.Id == arenaId);
        if (a is null) return null;
        int q = BattleReplay.Quantum;
        var z = a.GateZone;
        return new ArenaLayout(a.Info.Id, a.Cell, a.FullRows(), z is null ? 0 : (int)MathF.Round(z.Center.X * q), z is null ? 0 : (int)MathF.Round(z.Center.Y * q),
                               z is null ? 0 : (int)MathF.Round(z.Radius * q), a.Spec.GateFallsAtTick);
    }

    Task<NavMeshHandle> MeshOf(Arena a, CancellationToken ct)
    {
        if (!_baked.TryGetValue(a.Info.Id, out var t))
        {
            var (v, tri) = a.Geometry();
            _baked[a.Info.Id] = t = _baker.BakeAsync(v, tri);   // shared by every battle on this engine, so not tied to one caller's token
        }
        return t;
    }

    /// <summary>The baked mesh of an arena (baked on first use), for tests and tools that ask Tracer about the same ground.</summary>
    public Task<NavMeshHandle> BakedMeshAsync(string arenaId, CancellationToken ct = default)
        => MeshOf(_arenas.FirstOrDefault(a => a.Info.Id == arenaId) ?? _arenas[0], ct);

    public async ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct)
    {
        var arena = _arenas.FirstOrDefault(a => a.Info.Id == setup.ArenaId) ?? _arenas[0];
        var mesh = await MeshOf(arena, ct);
        return new TracerBattleSession(setup, arena, mesh, _tracer, _options, _engagement, _influence);
    }
}
