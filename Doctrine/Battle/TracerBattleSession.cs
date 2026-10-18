using System.Numerics;
using Tracer;
using Tracer.Abstractions;
using Tracer.Behaviors;
using Tracer.Integration;
using Tracer.Models;
using Tracer.Services;

namespace Doctrine.Battle;

/// <summary>How many times each Tracer service was asked, for tests and the hand-off table. Counters only; nothing reads them back into the game.</summary>
public sealed class TracerBattleStats
{
    public int NavigatorTicks { get; internal set; }
    public int AgentsPathed { get; internal set; }
    public int PathsNotFound { get; internal set; }
    public int EngagementCalls { get; internal set; }
    public int LineOfSightChecks;
    public int FogUpdates { get; internal set; }
    public int InfluenceMaps { get; internal set; }
    public int ObstacleUpdates { get; internal set; }
    public int GateFellAtTick { get; internal set; } = -1;
    public int Agents { get; internal set; }
}

/// <summary>
/// One battle on a baked Tracer navmesh. Cohorts are formation groups of agents (a scaled sample of the soldiers; the cohort's
/// STRENGTH is the soldier count and is the only truth about losses). Per tick:
/// FieldOrders = perception (fog of war, influence map, the falling gate) and each cohort's decision,
/// Move = Tracer's navigator (behaviours) paths every free agent and the agents step along their path,
/// Engage = Tracer's engagement service resolves who hits whom (deterministic resolver, line of sight, cover),
/// ApplyLosses = pending damage becomes soldiers lost and the agent sample thins to match, Record = one frame.
/// </summary>
public sealed class TracerBattleSession : IBattleSession
{
    const float VisionRange = 14f, FogCell = 4f, InfluenceRadius = 18f, ThreatRange = 24f;
    const int InfluenceEvery = 3;
    // A blocked zone isolates every triangle whose CENTROID is inside it, and a point lies within 1.9 of its triangle's centroid (2-unit cells),
    // so ground more than radius + 2 from the zone centre is always on an open triangle.
    const float SealMargin = 2.0f;

    sealed class Unit
    {
        public int Id, Index; public Cohort C = null!; public Vector2 P, Goal; public bool Alive = true, Fired; public long Taken;
        public FieldOrderKind Order; public IReadOnlyList<Vector2> Path = Array.Empty<Vector2>(); public bool PathFound; public string Behavior = "";
    }

    sealed class Cohort
    {
        public int Id, Role, Key, Slot, PowerPct, Start, Strength; public long PendingFp, CarryFp;
        public Side Side; public Trait Traits; public float Speed, Range; public int DamagePct, TakenPct;
        public List<Unit> Units = new(); public List<(FieldOrderKind Kind, int Weight)> Orders = new();
        public Vector2 Anchor, Centroid; public int Alive;
        public bool FlankSet, FlankDone; public Vector2 FlankPoint;
        public Vector2? Retreat;
        public PatrolBehavior? Patrol;
        public bool Engaged => Units.Any(u => u.Alive && u.Fired);
    }

    readonly BattleSetup _setup;
    readonly Arena _arena;
    readonly TracerBattleOptions _opt;
    readonly PathfindingWorld _world;
    readonly NavMeshHandle _baseMesh;
    NavMeshHandle _mesh;
    readonly List<Cohort> _cohorts = new();
    readonly IEngagementService _engagement;
    readonly IInfluenceMapService _influence;
    readonly ILineOfSightService _los;
    readonly IFogOfWarService _fog;
    readonly IDynamicObstacleService? _obstacles;
    readonly FogOfWarGrid?[] _grid = new FogOfWarGrid?[2];
    readonly Vector2[][] _known = new Vector2[2][];
    readonly float[]?[] _infl = new float[2][];
    readonly bool _needsInfluence;
    readonly SeekBehavior _seek = new();
    readonly RetreatBehavior _retreat;
    INavigatorService _navigator = null!;
    readonly List<ReplayEvent> _events = new();
    readonly Dictionary<(int, int), long> _pairFp = new();
    bool _gateFallen;
    Vector2 _gateCentre; float _gateRadius;

    public TracerBattleStats Stats { get; } = new();
    public int Tick { get; private set; }

    internal TracerBattleSession(BattleSetup setup, Arena arena, NavMeshHandle mesh, TracerContext tracer, TracerBattleOptions options,
                                 IEngagementService engagement, IInfluenceMapService influence)
    {
        _setup = setup; _arena = arena; _opt = options; _world = tracer.World; _baseMesh = mesh; _mesh = mesh;
        _engagement = engagement; _influence = influence;
        _los = _world.CreateLineOfSightService();
        _fog = _world.CreateFogOfWarService();
        if (arena.GateZone is { } z && options.DynamicObstacles)
        {
            _obstacles = _world.CreateDynamicObstacleService("doctrine-gate");
            _gateCentre = z.Center; _gateRadius = z.Radius;
        }
        _retreat = new RetreatBehavior(id => CohortOfUnit(id).Retreat is { } r && UnitOf(id).Order == FieldOrderKind.FallBack ? r : null);

        BuildCohorts();
        _needsInfluence = options.Influence && _cohorts.Any(c => c.Orders.Any(o => o.Kind is FieldOrderKind.Flank or FieldOrderKind.FallBack or FieldOrderKind.Guard));
        for (int r = 0; r < 2; r++)
        {
            _known[r] = new Vector2[_cohorts.Count];
            foreach (var e in _cohorts.Where(c => c.Role != r)) _known[r][e.Id] = e.Centroid;   // both sides know where the other deploys
            if (options.FogOfWar) _grid[r] = _fog.CreateGrid(mesh, new FogOfWarOptions(CellSize: FogCell, Mode: FogMode.ExploredPersists));
        }
        RebuildNavigator();
    }

    Cohort CohortOfUnit(int unitId) => _cohorts[unitId >> 8];
    Unit UnitOf(int unitId) => _cohorts[unitId >> 8].Units[unitId & 255];

    void RebuildNavigator()
    {
        var b = _world.CreateNavigator("doctrine-battle").WithMesh(_mesh).WithBehavior(_seek);
        foreach (var c in _cohorts) if (c.Patrol is { } p) b.WithBehavior(new GatedBehavior(p, id => (id >> 8) == c.Id && UnitOf(id).Order == FieldOrderKind.Hold, 3f));
        _navigator = b.WithBehavior(_retreat).Build();
    }

    // ---- set-up -----------------------------------------------------------------------------------------------------------

    void BuildCohorts()
    {
        var sides = new[] { _setup.Attacker, _setup.Defender };
        var raw = new List<(int Role, CohortRead Read, BattleSide Side)>();
        for (int r = 0; r < 2; r++) foreach (var c in sides[r].Cohorts.Where(c => c.Count > 0)) raw.Add((r, c, sides[r]));

        // The agent sample: about 3 x sqrt(soldiers), capped per cohort, then scaled down together if the field would exceed the cap.
        var agents = raw.Select(x => Math.Clamp((int)MathF.Round(MathF.Sqrt(x.Read.Count) * 3f), 1, Math.Min(_opt.MaxAgentsPerCohort, x.Read.Count))).ToArray();
        int total = agents.Sum();
        if (total > _opt.MaxAgents) for (int i = 0; i < agents.Length; i++) agents[i] = Math.Max(1, agents[i] * _opt.MaxAgents / total);

        for (int i = 0; i < raw.Count; i++)
        {
            var (role, read, side) = raw[i];
            var c = new Cohort
            {
                Id = i, Role = role, Side = side.Side, Key = read.Key, Slot = read.Slot, Traits = read.Traits, PowerPct = read.PowerPct,
                Start = read.Count, Strength = read.Count, Speed = UnitRules.Speed(read.Traits), Range = UnitRules.Range(read.Traits),
                DamagePct = UnitRules.DamagePct(read.Traits), TakenPct = UnitRules.TakenPct(read.Traits),
            };
            // Orders: every order named for this group, weighted; none named means Advance.
            foreach (var o in side.Orders.Where(o => o.Group == read.Slot && o.Weight > 0)) c.Orders.Add((o.Kind, o.Weight));
            if (c.Orders.Count == 0) c.Orders.Add((FieldOrderKind.Advance, 1));
            _cohorts.Add(c);
        }

        // Deployment: the front slot nearest the middle, one lane per cohort of a slot.
        foreach (var grp in _cohorts.GroupBy(c => (c.Role, c.Slot)))
        {
            var list = grp.OrderBy(c => c.Key).ToList();
            for (int j = 0; j < list.Count; j++)
            {
                var c = list[j];
                float depth = _arena.Width * (0.30f - 0.10f * Math.Clamp(c.Slot, 0, 2));
                float x = c.Role == 0 ? depth : _arena.Width - depth;
                float y = _arena.Height * (j + 1) / (list.Count + 1);
                c.Anchor = _arena.Snap(new Vector2(x, y));
            }
        }

        foreach (var c in _cohorts)
        {
            int n = agents[c.Id];
            var rng = new GameRng(GameRng.Mix(_setup.Seed, c.Id));
            for (int k = 0; k < n; k++)
            {
                var jitter = new Vector2((rng.Next(1000) / 1000f - 0.5f) * 0.4f, (rng.Next(1000) / 1000f - 0.5f) * 0.4f);
                var u = new Unit { Id = (c.Id << 8) | k, Index = k, C = c, Order = OrderOf(c, k, n) };
                u.P = _arena.Snap(c.Anchor + FormationOffset(k, n, c.Role == 0 ? 1.0f : 1.0f) + jitter);
                u.Goal = u.P;
                c.Units.Add(u);
            }
            c.Alive = n;
            c.Centroid = Centroid(c);
            if (c.Orders.Any(o => o.Kind == FieldOrderKind.Hold))
            {
                var ring = new List<Vector2>();
                foreach (var d in new[] { new Vector2(-1.4f, -1.4f), new Vector2(1.4f, -1.4f), new Vector2(1.4f, 1.4f), new Vector2(-1.4f, 1.4f) }) ring.Add(_arena.Snap(c.Anchor + d));
                c.Patrol = new PatrolBehavior(ring, arrivalDistance: 0.6f);
            }
        }
        Stats.Agents = _cohorts.Sum(c => c.Units.Count);
    }

    /// <summary>Which order an agent follows: the orders of its group share the agents in proportion to their integer weights.</summary>
    static FieldOrderKind OrderOf(Cohort c, int k, int n)
    {
        if (c.Orders.Count == 1) return c.Orders[0].Kind;
        long total = c.Orders.Sum(o => o.Weight), pos = (2L * k + 1) * total, acc = 0;
        foreach (var (kind, w) in c.Orders) { acc += w; if (pos < acc * 2L * n) return kind; }
        return c.Orders[^1].Kind;
    }

    static Vector2 FormationOffset(int k, int n, float spacing)
    {
        int cols = (int)MathF.Ceiling(MathF.Sqrt(n)), rows = (n + cols - 1) / cols;
        return new Vector2((k / cols - (rows - 1) / 2f) * spacing, (k % cols - (cols - 1) / 2f) * spacing);
    }

    Vector2 Centroid(Cohort c)
    {
        Vector2 sum = Vector2.Zero; int n = 0;
        foreach (var u in c.Units) if (u.Alive) { sum += u.P; n++; }
        return n == 0 ? c.Centroid : sum / n;
    }

    // ---- status -------------------------------------------------------------------------------------------------------------

    int Strength(int role) => _cohorts.Where(c => c.Role == role).Sum(c => c.Strength);

    public BattleStatus Status
    {
        get
        {
            int a = Strength(0), d = Strength(1);
            if (a == 0 && d == 0) return BattleStatus.Draw;
            if (d == 0) return BattleStatus.AttackerWon;
            if (a == 0) return BattleStatus.DefenderWon;
            return BattleStatus.Running;
        }
    }

    // ---- stage: field orders ------------------------------------------------------------------------------------------------

    public async ValueTask FieldOrdersAsync(CancellationToken ct)
    {
        foreach (var c in _cohorts) c.Centroid = Centroid(c);
        await MaybeDropGateAsync(ct);
        Perceive();
        await UpdateInfluenceAsync(ct);
        foreach (var c in _cohorts) if (c.Strength > 0) Decide(c);
    }

    async ValueTask MaybeDropGateAsync(CancellationToken ct)
    {
        if (_obstacles is null || _gateFallen || Tick < _arena.Spec.GateFallsAtTick) return;
        // Anyone standing in the gateway when it falls would be sealed into an isolated triangle with no path out, so the rubble throws
        // them clear: each is set down just outside the zone on its own side of the wall.
        foreach (var c in _cohorts) foreach (var u in c.Units)
            if (u.Alive && Vector2.Distance(u.P, _gateCentre) <= _gateRadius + SealMargin)
                u.P = _arena.Snap(new Vector2(_gateCentre.X + (c.Role == 0 ? -1f : 1f) * (_gateRadius + SealMargin + 0.1f), u.P.Y));
        _mesh = await _obstacles.UpdateAsync(_baseMesh, new[] { new NavMeshBlockedZone(_gateCentre, _gateRadius) }, ct);
        _gateFallen = true; Stats.ObstacleUpdates++; Stats.GateFellAtTick = Tick;
        RebuildNavigator();
    }

    void Perceive()
    {
        for (int r = 0; r < 2; r++)
        {
            var grid = _grid[r];
            if (grid is not null)
            {
                var observers = new List<(AgentDto, VisionProfile)>();
                foreach (var c in _cohorts.Where(c => c.Role == r && c.Strength > 0))
                {
                    var alive = c.Units.Where(u => u.Alive).ToList();
                    if (alive.Count == 0) continue;
                    var vision = new VisionProfile(VisionRange);
                    observers.Add((new AgentDto(alive[0].Id, alive[0].P, alive[0].P), vision));
                    if (alive.Count > 1) observers.Add((new AgentDto(alive[^1].Id, alive[^1].P, alive[^1].P), vision));
                }
                _fog.Update(grid, observers, _mesh); Stats.FogUpdates++;
            }
            foreach (var e in _cohorts.Where(c => c.Role != r && c.Strength > 0))
            {
                bool seen = grid is null || e.Units.Any(u => u.Alive && _fog.GetVisibility(grid, u.P) == CellVisibility.Visible);
                if (seen) _known[r][e.Id] = e.Centroid;
            }
        }
    }

    async ValueTask UpdateInfluenceAsync(CancellationToken ct)
    {
        if (!_needsInfluence || (_infl[0] is not null && Tick % InfluenceEvery != 0)) return;
        for (int r = 0; r < 2; r++)
        {
            var sources = _cohorts.Where(c => c.Role == r && c.Strength > 0).Select(c => new InfluenceSourceDto(c.Centroid, c.Strength, InfluenceRadius)).ToList();
            var values = await _influence.ComputeAsync(_mesh, sources, ct);
            _infl[r] = values as float[] ?? values.ToArray();
            Stats.InfluenceMaps++;
        }
    }

    /// <summary>Own territory minus the enemy's at a point (positive = ours). Zero when the map is off.</summary>
    float Net(int role, Vector2 p)
    {
        if (_infl[role] is not { } own || _infl[1 - role] is not { } foe) return 0f;
        int t = _mesh.FindNearestTriangle(p);
        return t < 0 || t >= own.Length ? 0f : own[t] - foe[t];
    }

    /// <summary>The enemy's territory at a point: the influence map computed from the other side's cohorts. Zero when the map is off.</summary>
    float Threat(int role, Vector2 p)
    {
        if (_infl[1 - role] is not { } foe) return 0f;
        int t = _mesh.FindNearestTriangle(p);
        return t < 0 || t >= foe.Length ? 0f : foe[t];
    }

    Cohort? Nearest(Cohort c)
    {
        Cohort? best = null; float bd = float.MaxValue;
        foreach (var e in _cohorts) if (e.Role != c.Role && e.Strength > 0) { float d = Vector2.DistanceSquared(c.Centroid, _known[c.Role][e.Id]); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    Cohort? Weakest(Cohort c)
    {
        Cohort? best = null; float bd = float.MaxValue;
        foreach (var e in _cohorts)
        {
            if (e.Role == c.Role || e.Strength == 0) continue;
            float d = Vector2.DistanceSquared(c.Centroid, _known[c.Role][e.Id]);
            if (best is null || e.Strength < best.Strength || (e.Strength == best.Strength && d < bd)) { best = e; bd = d; }
        }
        return best;
    }

    static Vector2 Norm(Vector2 v, Vector2 fallback) => v.LengthSquared() > 1e-6f ? Vector2.Normalize(v) : fallback;

    Vector2 SnapGoal(Vector2 p)
    {
        if (_gateFallen && Vector2.Distance(p, _gateCentre) <= _gateRadius + SealMargin)
            p = _gateCentre + Norm(p - _gateCentre, Vector2.UnitX) * (_gateRadius + SealMargin + 0.1f);
        return _arena.Snap(p);
    }

    Vector2 GoalFor(Cohort c, FieldOrderKind kind)
    {
        switch (kind)
        {
            case FieldOrderKind.Hold: return c.Anchor;
            case FieldOrderKind.Hunt: return Weakest(c) is { } w ? _known[c.Role][w.Id] : c.Anchor;
            case FieldOrderKind.Flank: return FlankGoal(c);
            case FieldOrderKind.FallBack: return c.Retreat ?? c.Anchor;
            case FieldOrderKind.Guard: return GuardGoal(c);
            default: return Nearest(c) is { } e ? _known[c.Role][e.Id] : c.Anchor;
        }
    }

    Vector2 FlankGoal(Cohort c)
    {
        var target = Nearest(c);
        if (target is null || c.FlankDone) return Nearest(c) is { } e ? _known[c.Role][e.Id] : c.Anchor;
        if (!c.FlankSet)
        {
            var ke = _known[c.Role][target.Id];
            var axis = Norm(ke - c.Centroid, c.Role == 0 ? Vector2.UnitX : -Vector2.UnitX);
            var lat = new Vector2(-axis.Y, axis.X);
            var a = SnapGoal(ke + lat * 10f + axis * 3f); var b = SnapGoal(ke - lat * 10f + axis * 3f);
            float ta = Threat(c.Role, a), tb = Threat(c.Role, b);
            c.FlankPoint = ta < tb ? a : tb < ta ? b : (((_setup.Seed + c.Id) & 1) == 0 ? a : b);   // the side where the enemy's territory is thinner
            c.FlankSet = true;
        }
        if (Vector2.Distance(c.Centroid, c.FlankPoint) < 5f || c.Engaged) { c.FlankDone = true; return _known[c.Role][target.Id]; }
        return c.FlankPoint;
    }

    Vector2 GuardGoal(Cohort c)
    {
        // Guard the friendly cohort under the most pressure (enemy territory over its position), or the one nearest the enemy.
        Cohort? ally = null; float worst = float.MinValue;
        foreach (var a in _cohorts)
        {
            if (a.Role != c.Role || a.Id == c.Id || a.Strength == 0) continue;
            float pressure = _infl[c.Role] is null ? -(Nearest(a) is { } n ? Vector2.Distance(a.Centroid, _known[c.Role][n.Id]) : 0f) : -Net(c.Role, a.Centroid);
            if (ally is null || pressure > worst) { ally = a; worst = pressure; }
        }
        if (ally is null) return c.Anchor;
        var foe = Nearest(c);
        var toward = foe is null ? Vector2.Zero : Norm(_known[c.Role][foe.Id] - ally.Centroid, Vector2.Zero);
        return ally.Centroid + toward * 2.5f;
    }

    void RetreatFor(Cohort c)
    {
        c.Retreat = null;
        Vector2 sum = Vector2.Zero; int n = 0;
        foreach (var e in _cohorts) if (e.Role != c.Role && e.Strength > 0 && Vector2.Distance(c.Centroid, _known[c.Role][e.Id]) <= ThreatRange) { sum += _known[c.Role][e.Id]; n++; }
        if (n == 0) return;
        var away = Norm(c.Centroid - sum / n, c.Role == 0 ? -Vector2.UnitX : Vector2.UnitX);
        // Where to go: away from the threat, to the ground the enemy's influence reaches least (the first candidate when the map is off or tied).
        Vector2 best = default; float bt = float.MaxValue; bool first = true;
        foreach (var p in new[] { SnapGoal(c.Centroid + away * 8f), SnapGoal(c.Centroid + away * 14f), c.Anchor })
        {
            float th = Threat(c.Role, p);
            if (first || th < bt) { best = p; bt = th; first = false; }
        }        c.Retreat = best;
    }

    void Decide(Cohort c)
    {
        if (c.Orders.Any(o => o.Kind == FieldOrderKind.FallBack)) RetreatFor(c);
        var centre = new Dictionary<FieldOrderKind, Vector2>();
        foreach (var u in c.Units)
        {
            if (!u.Alive) continue;
            if (!centre.TryGetValue(u.Order, out var g)) centre[u.Order] = g = GoalFor(c, u.Order);
            u.Goal = SnapGoal(g + FormationOffset(u.Index, c.Units.Count, 0.7f));
        }
    }

    // ---- stage: move ----------------------------------------------------------------------------------------------------------

    public async ValueTask MoveAsync(CancellationToken ct)
    {
        var free = new List<AgentDto>();
        foreach (var c in _cohorts) foreach (var u in c.Units)
            if (u.Alive && !(u.Fired && u.Order != FieldOrderKind.FallBack))   // an agent that struck last tick stands and fights
                free.Add(new AgentDto(u.Id, u.P, u.Goal, Radius: 0.35f, MaxSpeed: c.Speed, GoalChanged: true));
        if (free.Count > 0)
        {
            var result = await _navigator.TickAsync(free, Tick, ct);
            Stats.NavigatorTicks++; Stats.AgentsPathed += free.Count;
            foreach (var p in result.Paths)
            {
                var u = UnitOf(p.AgentId);
                u.Path = p.Waypoints; u.PathFound = p.PathFound; u.Behavior = p.ActiveBehavior ?? "";
                if (!p.PathFound) Stats.PathsNotFound++;
            }
        }
        foreach (var c in _cohorts) foreach (var u in c.Units)
        {
            if (!u.Alive || (u.Fired && u.Order != FieldOrderKind.FallBack) || !u.PathFound) continue;
            float budget = c.Speed; var p = u.P;
            foreach (var wp in u.Path)
            {
                float d = Vector2.Distance(p, wp);
                if (d < 1e-4f) continue;
                if (d <= budget) { p = wp; budget -= d; }
                else { p += (wp - p) / d * budget; break; }
            }
            u.P = p;
        }
    }

    // ---- stage: engage ----------------------------------------------------------------------------------------------------------

    public async ValueTask EngageAsync(CancellationToken ct)
    {
        _events.Clear(); _pairFp.Clear();
        foreach (var c in _cohorts) foreach (var u in c.Units) u.Fired = false;

        var sides = new List<UnitAgent>[] { new(), new() };
        float radius = 0f;
        foreach (var c in _cohorts)
        {
            if (c.Strength == 0) continue;
            int alive = c.Units.Count(u => u.Alive);
            int w = (c.Strength + alive - 1) / alive;   // soldiers each agent stands for
            int dfp = (int)Math.Min((long)w * c.PowerPct * c.DamagePct / 100, UnitRules.MaxHit);
            radius = Math.Max(radius, c.Range);
            foreach (var u in c.Units) if (u.Alive) sides[c.Role].Add(new UnitAgent(u.Id, u.P, c.Range, c.Id, dfp, c.TakenPct, c.Traits));
        }
        if (sides[0].Count == 0 || sides[1].Count == 0) return;

        var outcomes = await _engagement.ResolveOutcomesAsync(sides[0], sides[1], radius, Resolve, ct);
        Stats.EngagementCalls++;
        foreach (var o in outcomes)
        {
            if (!o.Hit) continue;
            int fp = (int)MathF.Floor(o.Damage);   // the integer hit; the fraction only ranked targets for the per-agent attack budget
            var src = UnitOf(o.SourceId); var tgt = UnitOf(o.TargetId);
            src.Fired = true; tgt.Taken += fp; tgt.C.PendingFp += fp;
            _pairFp[(src.C.Id, tgt.C.Id)] = _pairFp.GetValueOrDefault((src.C.Id, tgt.C.Id)) + fp;
        }
        foreach (var kv in _pairFp.OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2))
            _events.Add(new ReplayEvent(kv.Key.Item1, kv.Key.Item2, (int)Math.Clamp((kv.Value + UnitRules.Fp / 2) / UnitRules.Fp, 1, int.MaxValue)));
    }

    /// <summary>
    /// The deterministic resolver handed to Tracer's engagement service (the service's own hit/miss rolls use an unseeded random source, which a
    /// recorded battle cannot use). The service still does the spatial hashing, the pairing and the per-agent attack budget; this decides, for one
    /// pair in reach, whether the way is clear and what each side deals. The damage carries a distance fraction so the budget keeps the nearest target.
    /// </summary>
    IEnumerable<EngagementOutcome> Resolve(UnitAgent a, UnitAgent d, float dist, IGameMath math)
    {
        bool aReaches = dist <= a.AttackRange, dReaches = dist <= d.AttackRange;
        if (!aReaches && !dReaches) yield break;
        if (_opt.LineOfSight)
        {
            Interlocked.Increment(ref Stats.LineOfSightChecks);
            if (!_los.HasLineOfSight(_mesh, a.Position, d.Position)) yield break;
        }
        if (aReaches) yield return Strike(a, d, dist);
        if (dReaches) yield return Strike(d, a, dist);
    }

    EngagementOutcome Strike(UnitAgent s, UnitAgent t, float dist)
    {
        int fp = UnitRules.Hit(s.DamageFp, s.Traits, t.TakenPct, t.Traits, _arena.InCover(t.Position));
        return new EngagementOutcome(s.Id, t.Id, fp + 0.49f * (1f - dist / s.AttackRange), Hit: true, Crit: false);
    }

    // ---- stage: losses, record, finish -------------------------------------------------------------------------------------------

    public ValueTask ApplyLossesAsync(CancellationToken ct)
    {
        foreach (var c in _cohorts)
        {
            if (c.Strength > 0 && c.PendingFp > 0)
            {
                long total = c.CarryFp + c.PendingFp;
                int loss = (int)Math.Min(c.Strength, total / UnitRules.Fp);
                c.Strength -= loss;
                c.CarryFp = c.Strength == 0 ? 0 : total - (long)loss * UnitRules.Fp;
            }
            c.PendingFp = 0;
            // The agent sample thins with the cohort: the most battered agents fall first.
            int n = c.Units.Count;
            int want = c.Strength == 0 ? 0 : Math.Max(1, (int)(((long)c.Strength * n + c.Start - 1) / c.Start));
            int alive = c.Units.Count(u => u.Alive);
            while (alive > want)
            {
                Unit? victim = null;
                foreach (var u in c.Units) if (u.Alive && (victim is null || u.Taken >= victim.Taken)) victim = u;
                victim!.Alive = false; alive--;
            }
            c.Alive = alive;
        }
        return ValueTask.CompletedTask;
    }

    List<ReplayBlob> Blobs()
    {
        var l = new List<ReplayBlob>(_cohorts.Count);
        foreach (var c in _cohorts)
        {
            var p = Centroid(c); c.Centroid = p;
            int x = Math.Clamp((int)MathF.Round(p.X * BattleReplay.Quantum), 0, (int)(_arena.Width * BattleReplay.Quantum));
            int y = Math.Clamp((int)MathF.Round(p.Y * BattleReplay.Quantum), 0, (int)(_arena.Height * BattleReplay.Quantum));
            l.Add(new ReplayBlob(c.Id, c.Side, c.Key, c.Slot, x, y, c.Strength));
        }
        return l;
    }

    /// <summary>The arena's changing state for the recording: the gate bit is set from the frame after the gate fell.</summary>
    int WorldFlags() => _gateFallen ? WorldFlag.GateFallen : 0;

    public ReplayFrame Snapshot() => new(Tick, Blobs(), Array.Empty<ReplayEvent>(), WorldFlags());

    public ReplayFrame Record()
    {
        Tick++;
        var frame = new ReplayFrame(Tick, Blobs(), _events.ToList(), WorldFlags());
        _events.Clear();
        return frame;
    }

    public BattleResult Finish(bool tickLimitReached)
    {
        var outcome = Status;
        if (outcome == BattleStatus.Running)
        {
            int a = Strength(0), d = Strength(1);
            outcome = a > d ? BattleStatus.AttackerWon : d > a ? BattleStatus.DefenderWon : BattleStatus.Draw;
        }
        IReadOnlyList<CohortLoss> Losses(int role) => _cohorts.Where(c => c.Role == role && c.Start > c.Strength)
            .Select(c => new CohortLoss(c.Key, c.Slot, c.Start - c.Strength)).ToList();
        return new BattleResult(outcome, Tick, Losses(0), Losses(1));
    }

    /// <summary>The behaviour Tracer's navigator chose for a cohort's first living agent on the last Move (for tests and the hand-off).</summary>
    public string BehaviorOf(int blobId) => _cohorts[blobId].Units.FirstOrDefault(u => u.Alive)?.Behavior ?? "";

    /// <summary>One agent as it stands now (a read for tests and the hand-off; the replay carries cohorts, not agents).</summary>
    public readonly record struct AgentState(int Id, int Blob, float X, float Y, bool Alive, FieldOrderKind Order, string Behavior, bool PathFound, float GoalX, float GoalY);

    public IReadOnlyList<AgentState> Agents()
    {
        var l = new List<AgentState>();
        foreach (var c in _cohorts) foreach (var u in c.Units) l.Add(new AgentState(u.Id, c.Id, u.P.X, u.P.Y, u.Alive, u.Order, u.Behavior, u.PathFound, u.Goal.X, u.Goal.Y));
        return l;
    }

    /// <summary>The arena's size in units.</summary>
    public (float Width, float Height) ArenaSize => (_arena.Width, _arena.Height);
    /// <summary>The gateway zone (centre, radius) of an arena with a gate, else null.</summary>
    public (float X, float Y, float R)? Gate => _arena.GateZone is { } z ? (z.Center.X, z.Center.Y, z.Radius) : null;
    /// <summary>The navmesh as it stands now (after any gate has fallen).</summary>
    public NavMeshHandle Mesh => _mesh;
    /// <summary>Whether a point is on walkable ground of the arena layout (cover counts, walls do not).</summary>
    public bool OnWalkableGround(float x, float y) => _arena.IsOpen(new Vector2(x, y));}
