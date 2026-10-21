using System.Numerics;
using EvalApp;
using Phasor;
using Tracer;
using Tracer.Abstractions;
using Tracer.Integration;
using Tracer.Models;
using Doctrine.Army;
using Doctrine.Battle;

namespace Doctrine.Lane;

/// <summary>A body of the field engine agent: Tracer's <see cref="GameAgent"/> plus the integers the deterministic resolver needs (same pattern as the old battle's UnitAgent).</summary>
internal sealed record LaneAgent(int Id, Vector2 Position, float AttackRange, int DamageFp, int TakenPct, Trait Traits, bool Ready)
    : GameAgent(Id, Position, Position, Radius: 0.35f, MaxSpeed: 0f, GoalChanged: false, Attack: DamageFp, Defense: TakenPct,
                Health: 1f, MaxHealth: 1f, AttackSpeed: 1f, AttackRange: AttackRange, MaxAttacksPerTick: 1);

/// <summary>
/// ONE continuous lane battle (the battler layer). Bodies spawn at a base, walk down their lane on the Tracer navmesh toward the enemy base and strike what they meet.
/// <para>
/// PHASES. <see cref="StepAsync"/> runs to the next pause (a multiple of <see cref="LaneSetup.PhaseTicks"/>) and stops there on the same tick for both players.
/// <see cref="ApplyPlays"/> is legal ONLY at a pause. Two clients with the same <see cref="LaneSetup"/>, seed and ordered plays hold bit-identical state; compare
/// <see cref="StateHash"/> at each pause.
/// </para>
/// <para>
/// PER TICK (one EvalApp pipeline, stages <c>Lane.Think</c>, <c>Lane.Move</c>, <c>Lane.Engage</c>, <c>Lane.Resolve</c>, <c>Lane.Judge</c>): bodies re-decide their goal from the lane's
/// order (staggered), Tracer's multi-agent service paths the ones that need it and the bodies step along their paths, Tracer's engagement service with a deterministic
/// resolver (line of sight over the baked mesh, cover, the counter ring) says who strikes whom, then damage is applied to every body at once.
/// </para>
/// <para>
/// WHERE THE BODIES LIVE. The roster (how many bodies of each kind stand in each lane, per side) is a Pleroma store per side, changed only by Pleroma Add. The continuous
/// state of a body (position, hit points, cooldown) is a host record: a Pleroma holds counts over keys, not a float payload per instance.
/// </para>
/// </summary>
public sealed class LaneBattle
{
    internal sealed class Body
    {
        public int Id, Seq, Lane, Kind, Hp, Cooldown, Still, Target = -1, PathIdx, RetryAt, Pending, DamageFp, TakenPct;
        public Side Side; public Trait Traits; public float Step, Range, Speed;
        public Vector2 P, Goal, Final, RequestedGoal, Face, Home;
        public IReadOnlyList<Vector2> Path = Array.Empty<Vector2>();
        public bool NeedsPath, Engaged, Struck, Hit, Moved, ForceThink;
    }

    readonly LaneSetup _setup;
    readonly int _seed;
    readonly LaneMap _map;
    readonly LaneProbe _probe;
    readonly NavMeshHandle _mesh;
    readonly IMultiAgentService _multi;
    readonly IEngagementService _engagement;
    readonly ILineOfSightService _los;
    readonly Pleroma[] _roster;                           // [side * lanes + lane]: one store per side and lane (a store reads back at most KeyCapacity = 8 items, one per kind)
    readonly FieldOrderKind[] _orders;                    // [side * lanes + lane]
    readonly long[] _baseHp = new long[2];                // [0] the player's base, [1] the enemy's
    readonly long[] _basePending = new long[2];
    readonly long _baseMax;
    readonly LaneContext _ctx;
    List<Body> _units = new();                        // in ID order, always (every stage walks it in that order, so the order plays were applied in cannot matter)
    readonly Dictionary<int, Body> _byId = new();     // lookup only; never enumerated
    readonly int[] _seq = new int[2];                 // bodies spawned so far, per side
    int _tick, _globalId;
    LaneStatus _status = LaneStatus.Running;
    readonly List<(int Tick, IReadOnlyList<LanePlay> Plays)> _playLog = new();
    readonly List<(int Tick, ulong Hash)> _pauseLog = new();

    // reused per-tick buffers
    readonly List<AgentDto> _req = new();
    readonly List<Body> _wait = new();
    readonly List<LaneAgent> _a0 = new(), _a1 = new();

    public LaneStats Stats { get; } = new();
    public LaneSetup Setup => _setup;
    public int Seed => _seed;
    public int Tick => _tick;
    public int PhaseTicks => _setup.PhaseTicks;
    public int Phase => _tick / _setup.PhaseTicks;
    public LaneStatus Status => _status;
    /// <summary>True on a pause: the tick is a multiple of the phase length. Plays are legal here (while the match is running) and nowhere else.</summary>
    public bool AtPause => _tick % _setup.PhaseTicks == 0;
    /// <summary>Ticks until the next pause (a full phase when already at one).</summary>
    public int TicksToPause => _setup.PhaseTicks - _tick % _setup.PhaseTicks;
    public long PlayerBaseHp => _baseHp[0];
    public long EnemyBaseHp => _baseHp[1];
    public long BaseMaxHp => _baseMax;
    public int LiveBodies => _units.Count;
    public LaneLayout Layout => _map.Layout();
    /// <summary>The baked navmesh (for tests and tools that ask Tracer about the same ground).</summary>
    public NavMeshHandle Mesh => _mesh;

    LaneBattle(LaneSetup setup, int seed, LaneMap map, NavMeshHandle mesh, TracerContext? tracer, PathfindingWorld world)
    {
        _setup = setup; _seed = seed; _map = map; _mesh = mesh; _probe = setup.Probe;
        _multi = world.CreateMultiAgentService("lane-multi");
        _engagement = tracer?.EngagementService ?? world.CreateEngagementService("lane-engagement");
        _los = world.CreateLineOfSightService();
        _roster = new Pleroma[2 * setup.Lanes];
        for (int i = 0; i < _roster.Length; i++) _roster[i] = new Pleroma(PleromaArmyLayout.ArmyShape);
        _orders = new FieldOrderKind[2 * setup.Lanes];   // Advance (0) everywhere
        _baseMax = (long)setup.BaseHp * UnitRules.Fp;
        _baseHp[0] = _baseHp[1] = _baseMax;
        _ctx = new LaneContext(this);
        var app = Eval.App("DoctrineLane").WithContext(_ctx);
        app.DefineDomain("Lane", new LaneDomain())
            .DefineTask<LaneRun>("LaneTick")
                .AddStep<LaneThinkStage>(LaneStages.Think)
                .AddStep<LaneMoveStage>(LaneStages.Move)
                .AddStep<LaneEngageStage>(LaneStages.Engage)
                .AddStep<LaneResolveStage>(LaneStages.Resolve)
                .AddStep<LaneJudgeStage>(LaneStages.Judge)
            .Run(_ctx.Tick)
            .DefineTask<LaneRun>("LanePhase")
                .AddStep<LanePhaseStage>(LaneStages.Phase)
            .Run(_ctx.Phase)
            .Build();
        _pauseLog.Add((0, StateHash()));
    }

    /// <summary>
    /// Bakes the lanes and creates the match at tick 0 (a pause: the first plays go in now). Async because Tracer's baker and pipelines are; in a browser await it.
    /// <paramref name="tracer"/> is the game's Tracer context (its world, and its engagement service if it carries one); null = a private world.
    /// </summary>
    public static async Task<LaneBattle> CreateAsync(LaneSetup setup, int seed, TracerContext? tracer = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.Validate();
        var map = LaneMap.Build(setup.Lanes, setup.LaneCols);
        var world = tracer?.World ?? new PathfindingWorld();
        var (v, t) = map.Geometry();
        var mesh = await world.CreateNavMeshBakerService("lane-baker").BakeAsync(v, t, null, ct);
        return new LaneBattle(setup, seed, map, mesh, tracer, world);
    }

    // ---- plays (a pause only) --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies the plays in order. Legal ONLY at a pause of a running match: anywhere else it throws and changes nothing. A refused play changes nothing and says why;
    /// the list is recorded (the replay is the seed plus these).
    /// </summary>
    public IReadOnlyList<LanePlayResult> ApplyPlays(IReadOnlyList<LanePlay> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);
        var results = new List<LanePlayResult>(plays.Count);
        if (_status != LaneStatus.Running)
        {
            for (int i = 0; i < plays.Count; i++) results.Add(new(i, false, 0, "the match is over"));
            return results;
        }
        if (!AtPause && !_probe.HasFlag(LaneProbe.PlaysMidPhase))
            throw new InvalidOperationException($"plays are applied only at a pause; tick {_tick} is {_tick % _setup.PhaseTicks} ticks into a phase");
        if (plays.Count > 0) _playLog.Add((_tick, plays.ToArray()));
        for (int i = 0; i < plays.Count; i++)
        {
            Stats.Plays++;
            results.Add(plays[i] switch
            {
                SpawnPlay sp => Spawn(i, sp),
                OrderPlay op => Order(i, op),
                _ => new LanePlayResult(i, false, 0, "unknown play"),
            });
        }
        return results;
    }

    LanePlayResult Spawn(int index, SpawnPlay p)
    {
        int lane = _probe.HasFlag(LaneProbe.SpawnIgnoresLane) ? 0 : p.Lane;
        if ((uint)lane >= (uint)_setup.Lanes) return new(index, false, 0, "no such lane");
        if ((uint)p.Kind >= (uint)Roster.Size) return new(index, false, 0, "no such kind");
        if (p.Side is not (Side.Player or Side.Enemy)) return new(index, false, 0, "no such side");
        if (p.Count <= 0) return new(index, false, 0, "count must be positive");
        int s = (int)p.Side, live = 0;
        foreach (var b in _units) if (b.Side == p.Side) live++;
        int room = _probe.HasFlag(LaneProbe.CapOnBodiesIgnored) ? int.MaxValue : _setup.MaxUnitsPerSide - live;
        int n = Math.Min(p.Count, room);
        if (n <= 0) return new(index, false, 0, "the side is at the body cap");
        var traits = Roster.TraitsOf(p.Kind);
        float dir = s == 0 ? 1f : -1f;
        for (int j = 0; j < n; j++)
        {
            // The id is also the body's priority in Tracer's conflict resolution (the LOWER id keeps its path, the higher one sidesteps), and the stagger of its strikes and thinking is
            // read off its place in its side's spawn order. Both are derived from the side-local sequence number, and the id interleaves the two sides (at each sequence number the
            // side that takes the even id alternates), so neither side owns the right of way or the early strike (the old battle's UnitIdOf, for the same reason).
            int seq = _seq[s]++;
            int id = _probe.HasFlag(LaneProbe.IdsInPlayOrder) ? ++_globalId : (seq << 1) | ((s ^ (seq & 1)) & 1);
            var b = new Body
            {
                Id = id, Seq = seq, Side = p.Side, Lane = lane, Kind = p.Kind, Traits = traits, Hp = UnitRules.Fp,
                Speed = UnitRules.Speed(traits), Step = LaneRules.StepOf(traits), Range = UnitRules.Range(traits),
                DamageFp = 100 * UnitRules.DamagePct(traits) / 100, TakenPct = UnitRules.TakenPct(traits),
                Cooldown = seq % LaneRules.StrikeEvery, NeedsPath = true, ForceThink = true, Face = new Vector2(dir, 0f),
            };
            // the batch stands in a block inside the base zone, in front of the lane's mouth: 7 rows by up to 9 columns, then wraps
            int row = j % 7, col = (j / 7) % 9;
            ulong r = _probe.HasFlag(LaneProbe.UnseededJitter) ? (ulong)Random.Shared.NextInt64() : GameRng.Mix(_seed, id);
            float jx = ((r & 0xFFFF) / 65535f - 0.5f) * 0.3f, jy = (((r >> 16) & 0xFFFF) / 65535f - 0.5f) * 0.3f;
            float x = s == 0 ? _map.PlayerEdgeX - 1f - col : _map.EnemyEdgeX + 1f + col;
            b.P = _map.Arena.Snap(new Vector2(x + jx, _map.LaneCentreY[lane] + (row - 3) * 1.1f + jy));
            b.Home = b.P; b.Goal = b.P; b.Final = b.P; b.RequestedGoal = b.P;
            _units.Add(b); _byId[id] = b;
        }
        _units.Sort(static (x, y) => x.Id.CompareTo(y.Id));
        _roster[s * _setup.Lanes + lane].Add(new HologramItem((ulong)p.Kind, (uint)traits, PleromaArmyLayout.SlotArrangement[lane], 0), n);
        Stats.Spawned += n;
        return new(index, true, n, n < p.Count ? "cut short by the body cap" : "");
    }

    LanePlayResult Order(int index, OrderPlay p)
    {
        if (p.Side is not (Side.Player or Side.Enemy)) return new(index, false, 0, "no such side");
        if (p.Lane < -1 || p.Lane >= _setup.Lanes) return new(index, false, 0, "no such lane");
        if (p.Order is not (FieldOrderKind.Advance or FieldOrderKind.Hold or FieldOrderKind.FallBack or FieldOrderKind.Hunt or FieldOrderKind.Guard))
            return new(index, false, 0, "this order has no meaning in a lane");
        if (_probe.HasFlag(LaneProbe.OrderIgnored)) return new(index, true, 0, "");
        int s = (int)p.Side, touched = 0;
        for (int lane = 0; lane < _setup.Lanes; lane++)
        {
            if (p.Lane != -1 && p.Lane != lane) continue;
            _orders[s * _setup.Lanes + lane] = p.Order;
        }
        foreach (var b in _units)
            if (b.Side == p.Side && (p.Lane == -1 || b.Lane == p.Lane)) { b.ForceThink = true; touched++; }
        return new(index, true, touched, "");
    }

    /// <summary>The order a side's bodies follow in a lane now.</summary>
    public FieldOrderKind OrderOf(Side side, int lane) => _orders[(int)side * _setup.Lanes + lane];

    // ---- stepping --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs to the next pause (a full phase when already at one), or <paramref name="maxTicks"/> ticks if fewer, or until the match ends. Stepping in pieces gives exactly the
    /// state of one long run. Plays are legal again once <see cref="AtPause"/> is true.
    /// </summary>
    public async Task<LaneStepResult> StepAsync(int maxTicks = int.MaxValue, CancellationToken ct = default)
    {
        if (_status != LaneStatus.Running) return new(0, _tick, AtPause, _status, HashHere());
        if (maxTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxTicks));
        int n = Math.Min(maxTicks, TicksToPause);
        if (_probe.HasFlag(LaneProbe.PhaseOffByOne) && n == TicksToPause) n++;
        if (_probe.HasFlag(LaneProbe.CooldownResetsPerStep)) foreach (var b in _units) b.Cooldown = b.Seq % LaneRules.StrikeEvery;
        int before = _tick;
        var r = await _ctx.Phase.RunAsync(new LaneRun(n), ct);
        PipelineFailure.DataOrThrow(r, $"phase at tick {before} of the lane battle (seed {_seed})");
        return new(_tick - before, _tick, AtPause, _status, HashHere());
    }

    /// <summary>
    /// Runs on to tick <paramref name="tick"/> (or until the match ends) in ONE run, passing every pause on the way without stopping. No play can be applied on the way, so this
    /// is the same match as stepping phase by phase (and a proof checks that it is); the pause hashes are still logged for the replay. For tools and solo play, not for a PvP client.
    /// </summary>
    public async Task<LaneStepResult> RunToAsync(int tick, CancellationToken ct = default)
    {
        if (_status != LaneStatus.Running || tick <= _tick) return new(0, _tick, AtPause, _status, HashHere());
        int before = _tick;
        var r = await _ctx.Phase.RunAsync(new LaneRun(tick - _tick), ct);
        PipelineFailure.DataOrThrow(r, $"run from tick {before} of the lane battle (seed {_seed})");
        return new(_tick - before, _tick, AtPause, _status, HashHere());
    }

    /// <summary>The hash a step reports: the state hash on a pause or at the end (already taken by the judge, so not paid twice), 0 in the middle of a phase (the roster read behind it costs milliseconds).</summary>
    ulong HashHere()
    {
        if (!AtPause && _status == LaneStatus.Running) return 0;
        return _pauseLog.Count > 0 && _pauseLog[^1].Tick == _tick ? _pauseLog[^1].Hash : StateHash();
    }

    // ---- the tick stages (called by the pipeline's steps) -----------------------------------------------------------------------

    internal void ThinkStage()
    {
        foreach (var u in _units)
        {
            if (!u.ForceThink && (_tick + u.Seq) % LaneRules.ThinkEvery != 0) continue;
            u.ForceThink = false;
            // WHERE TO PATH and WHERE TO STAND. Bodies of a lane that follow the same order path to ONE shared point (Tracer shares one guidance field among agents with the same
            // goal, so a whole lane costs one field, not one per body); each then steps to its own place in the block (Final) by a short straight walk on open ground.
            Vector2 goal, final;
            float dir = u.Side == Side.Player ? 1f : -1f;
            var offset = new Vector2(-dir * (u.Seq / 7 % 5) * 0.7f, (u.Seq % 7 - 3) * 0.9f);   // behind the front, spread across the lane
            switch (_orders[(int)u.Side * _setup.Lanes + u.Lane])
            {
                case FieldOrderKind.Hold:
                    goal = new Vector2(OwnEdge(u) + dir * _map.LaneLength * LaneRules.HoldPct / 100f, _map.LaneCentreY[u.Lane]); final = goal + offset; break;
                case FieldOrderKind.FallBack:
                    goal = new Vector2(OwnEdge(u) - dir * 5f, _map.LaneCentreY[u.Lane]); final = u.Home; break;
                case FieldOrderKind.Guard:
                {
                    Vector2 sum = Vector2.Zero; int n = 0;
                    foreach (var f in _units) if (f.Side == u.Side && f.Lane == u.Lane) { sum += f.P; n++; }
                    var c = sum / n;
                    goal = Lattice(c); final = c + offset * 0.4f; break;
                }
                case FieldOrderKind.Hunt when Weakest(u) is { } w:
                    goal = Lattice(w.P); final = w.P + offset * 0.3f; break;
                case FieldOrderKind.Advance when Nearest(u, LaneRules.SightRange) is { } e:
                    goal = Lattice(e.P); final = e.P + offset * 0.3f; break;
                default:   // Advance with nobody in sight, Hunt with nobody to hunt: down the lane to the enemy's edge
                    goal = new Vector2(EnemyEdge(u) - dir * 0.5f, _map.LaneCentreY[u.Lane]); final = goal + offset; break;
            }
            goal = _map.Arena.Snap(goal);
            u.Goal = goal; u.Final = _map.Arena.Snap(final);
            if (Vector2.Distance(goal, u.RequestedGoal) > LaneRules.RepathDistance) u.NeedsPath = true;
        }
    }

    float OwnEdge(Body u) => u.Side == Side.Player ? _map.PlayerEdgeX : _map.EnemyEdgeX;
    float EnemyEdge(Body u) => u.Side == Side.Player ? _map.EnemyEdgeX : _map.PlayerEdgeX;

    /// <summary>A point snapped to a 4-unit lattice, so bodies chasing neighbouring targets share one goal (and one guidance field).</summary>
    static Vector2 Lattice(Vector2 p) => new(MathF.Round(p.X / 4f) * 4f, MathF.Round(p.Y / 4f) * 4f);

    Body? Nearest(Body u, float range)
    {
        Body? best = null; float bd = range * range;
        foreach (var e in _units)
        {
            if (e.Side == u.Side || e.Lane != u.Lane) continue;
            float d = Vector2.DistanceSquared(u.P, e.P);
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    Body? Weakest(Body u)
    {
        Body? best = null; float bd = 0f;
        float lim = LaneRules.HuntRange * LaneRules.HuntRange;
        foreach (var e in _units)
        {
            if (e.Side == u.Side || e.Lane != u.Lane) continue;
            float d = Vector2.DistanceSquared(u.P, e.P);
            if (d > lim) continue;
            if (best is null || e.Hp < best.Hp || (e.Hp == best.Hp && d < bd)) { best = e; bd = d; }
        }
        return best;
    }

    Body? Find(int id) => _byId.GetValueOrDefault(id);

    internal async ValueTask MoveStage(CancellationToken ct)
    {
        _req.Clear();
        // At most MaxPathsPerTick requests a tick, bodies with no path at all first (a fresh spawn), then the rest, each group in ID order. A spawn burst is spread over the next
        // ticks, not paid in one; and the id interleaves the two sides, so a burst of equal size from both sides is served side by side, not one army before the other.
        _wait.Clear();
        foreach (var u in _units)
            if (u.NeedsPath && !u.Engaged && _tick >= u.RetryAt) _wait.Add(u);
        if (_wait.Count > 0)
        {
            _wait.Sort(static (x, y) => (x.Path.Count == 0) != (y.Path.Count == 0) ? (x.Path.Count == 0 ? -1 : 1) : x.Id.CompareTo(y.Id));
            for (int i = 0; i < _wait.Count && i < LaneRules.MaxPathsPerTick; i++)
            {
                var u = _wait[i];
                _req.Add(new AgentDto(u.Id, u.P, u.Goal, Radius: 0.35f, MaxSpeed: u.Speed, GoalChanged: true));
            }
        }
        if (_req.Count > 0)
        {
            var result = await _multi.TickAsync(_req, _mesh, _tick, null, ct);
            Stats.PathRequests += _req.Count;
            foreach (var p in result.Paths)
            {
                var u = Find(p.AgentId);
                if (u is null) continue;
                u.Path = p.Waypoints; u.PathIdx = 0; u.RequestedGoal = u.Goal;
                if (p.PathFound) u.NeedsPath = false;
                else { u.NeedsPath = true; u.RetryAt = _tick + 10; Stats.PathsNotFound++; }
            }
        }
        foreach (var u in _units)
        {
            u.Moved = false;
            if (u.Engaged || u.Path.Count == 0) continue;
            float budget = u.Step; var p = u.P;
            while (u.PathIdx < u.Path.Count)
            {
                var wp = u.Path[u.PathIdx];
                float d = Vector2.Distance(p, wp);
                if (d <= budget) { p = wp; budget -= d; u.PathIdx++; }
                else { p += (wp - p) / d * budget; budget = 0f; break; }
            }
            if (u.PathIdx >= u.Path.Count && budget > 0f && !u.NeedsPath)
            {
                // at the shared point: walk straight to the body's own place in the block (a short step on open ground)
                var to = u.Final - p; float d = to.Length();
                if (d > 1e-3f)
                {
                    var q = d <= budget ? u.Final : p + to / d * budget;
                    if (_map.Arena.IsOpen(q)) p = q;
                }
            }
            var delta = p - u.P;
            if (delta.LengthSquared() > 1e-8f) { u.Face = Vector2.Normalize(delta); u.Moved = true; u.P = p; }
        }
    }

    bool BaseInReach(Body u) => u.Side == Side.Player ? u.P.X + u.Range >= _map.EnemyEdgeX : u.P.X - u.Range <= _map.PlayerEdgeX;

    int StrikeDamage(Body u)
    {
        long d = u.DamageFp;
        if (u.Still >= LaneRules.SetTicks) d = d * (100 + UnitRules.SetBonusPct(u.Traits)) / 100;   // dug in: a held line strikes harder
        return (int)Math.Min(d, UnitRules.MaxHit);
    }

    internal async ValueTask EngageStage(CancellationToken ct)
    {
        foreach (var u in _units) { u.Engaged = false; u.Struck = false; u.Target = -1; }
        _a0.Clear(); _a1.Clear();
        float radius = UnitRules.MeleeRange;
        foreach (var u in _units)
        {
            radius = Math.Max(radius, u.Range);
            (u.Side == Side.Player ? _a0 : _a1).Add(new LaneAgent(u.Id, u.P, u.Range, StrikeDamage(u), u.TakenPct, u.Traits, u.Cooldown == 0));
        }
        if (_a0.Count > 0 && _a1.Count > 0)
        {
            var outcomes = await _engagement.ResolveOutcomesAsync(_a0, _a1, radius, Resolve, ct);
            Stats.EngagementCalls++;
            var list = new List<EngagementOutcome>(outcomes);
            list.Sort(static (x, y) => x.SourceId != y.SourceId ? x.SourceId.CompareTo(y.SourceId) : x.TargetId.CompareTo(y.TargetId));
            foreach (var o in list)
            {
                var src = Find(o.SourceId); var tgt = Find(o.TargetId);
                if (src is null || tgt is null) continue;
                src.Engaged = true;
                if (!o.Hit) { if (src.Target < 0) { src.Target = tgt.Id; Face(src, tgt.P); } continue; }
                int fp = (int)MathF.Floor(o.Damage);   // the integer hit; the fraction only ranked the targets for the per-agent budget
                src.Struck = true; src.Target = tgt.Id; src.Cooldown = LaneRules.StrikeEvery; Face(src, tgt.P);
                tgt.Pending += fp; Stats.Strikes++;
            }
        }
        foreach (var u in _units)
        {
            if (!BaseInReach(u)) continue;
            u.Engaged = true;
            if (u.Struck || u.Cooldown != 0) continue;
            _basePending[u.Side == Side.Player ? 1 : 0] += StrikeDamage(u);
            u.Struck = true; u.Cooldown = LaneRules.StrikeEvery; Stats.BaseStrikes++;
            Face(u, new Vector2(u.Side == Side.Player ? _map.Width : 0f, u.P.Y));
        }
        foreach (var u in _units) if (u.Cooldown > 0) u.Cooldown--;
    }

    static void Face(Body u, Vector2 at)
    {
        var d = at - u.P;
        if (d.LengthSquared() > 1e-8f) u.Face = Vector2.Normalize(d);
    }

    IEnumerable<EngagementOutcome> Resolve(LaneAgent a, LaneAgent d, float dist, IGameMath math)
    {
        bool aReach = dist <= a.AttackRange, dReach = dist <= d.AttackRange;
        if (!aReach && !dReach) yield break;
        Interlocked.Increment(ref Stats.LineOfSightChecks);
        if (!_los.HasLineOfSight(_mesh, a.Position, d.Position)) yield break;   // walls stop a strike
        if (aReach) yield return Strike(a, d, dist);
        if (dReach) yield return Strike(d, a, dist);
    }

    EngagementOutcome Strike(LaneAgent s, LaneAgent t, float dist)
    {
        if (!s.Ready) return new EngagementOutcome(s.Id, t.Id, 0f, Hit: false, Crit: false);   // in contact, not yet ready: it stands and waits
        bool cover = _map.Arena.InCover(t.Position);
        int fp = UnitRules.Hit(s.DamageFp, s.Traits, t.TakenPct, t.Traits, cover, !_probe.HasFlag(LaneProbe.CountersOff), _probe.HasFlag(LaneProbe.CountersReversed));
        return new EngagementOutcome(s.Id, t.Id, fp + 0.49f * (1f - dist / s.AttackRange), Hit: true, Crit: false);
    }

    internal void ResolveStage()
    {
        List<Body>? dead = null;
        foreach (var u in _units)
        {
            u.Hit = u.Pending > 0;
            u.Hp -= u.Pending; u.Pending = 0;
            if (u.Hp <= 0) (dead ??= new()).Add(u);
            else u.Still = (u.Moved || u.Hit) ? 0 : Math.Min(u.Still + 1, LaneRules.SetTicks);
        }
        if (dead is not null)
        {
            var keep = new List<Body>(_units.Count - dead.Count);
            foreach (var u in _units) if (u.Hp > 0) keep.Add(u);
            _units = keep;
            foreach (var u in dead)
            {
                Stats.Deaths++; _byId.Remove(u.Id);
                if (!_probe.HasFlag(LaneProbe.RosterKeepsTheDead))
                    _roster[(int)u.Side * _setup.Lanes + u.Lane].Add(new HologramItem((ulong)u.Kind, (uint)u.Traits, PleromaArmyLayout.SlotArrangement[u.Lane], 0), -1);
            }
        }
        for (int s = 0; s < 2; s++) { _baseHp[s] = Math.Max(0, _baseHp[s] - _basePending[s]); _basePending[s] = 0; }
        _tick++; Stats.Ticks++;
    }

    /// <summary>After the tick: a fallen base ends the match (both at once is a draw); the hard cap is a draw. Returns true when the match is over.</summary>
    internal bool JudgeStage()
    {
        if (_status != LaneStatus.Running) return true;
        bool playerBaseFell = _baseHp[0] <= 0, enemyBaseFell = _baseHp[1] <= 0;
        if ((playerBaseFell || enemyBaseFell) && !_probe.HasFlag(LaneProbe.BaseFallDoesNotEnd))
            _status = playerBaseFell && enemyBaseFell ? (_probe.HasFlag(LaneProbe.DrawIsAWin) ? LaneStatus.PlayerWon : LaneStatus.Draw)
                    : enemyBaseFell ? LaneStatus.PlayerWon : LaneStatus.EnemyWon;
        else if (_tick >= _setup.TickCap && !_probe.HasFlag(LaneProbe.CapIgnored)) _status = LaneStatus.Draw;
        bool over = _status != LaneStatus.Running;
        if (_probe.HasFlag(LaneProbe.SurvivorsClearedAtPause) && AtPause) _units = new List<Body>();
        if (AtPause || over) _pauseLog.Add((_tick, StateHash()));   // every pause passed, and the tick the match ended on
        return over;
    }

    // ---- reads -------------------------------------------------------------------------------------------------------------------

    /// <summary>The field now, for a renderer: bodies in id order with position, facing, hit points, kind, side and lane, the two bases, the tick.</summary>
    public LaneSnapshot Snapshot()
    {
        var l = new List<LaneUnitView>(_units.Count);
        foreach (var u in _units)
            l.Add(new(u.Id, u.Side, u.Lane, u.Kind, u.P.X, u.P.Y, u.Face.X, u.Face.Y, u.Hp, UnitRules.Fp, u.Target, _orders[(int)u.Side * _setup.Lanes + u.Lane]));
        return new(_tick, Phase, _status, _baseHp[0], _baseHp[1], _baseMax, l);
    }

    /// <summary>Bodies of a kind standing in a lane for a side, read from the Pleroma roster (not from the bodies).</summary>
    public long RosterCount(Side side, int lane, int kind)
    {
        if (!_roster[(int)side * _setup.Lanes + lane].TryItems(out var items)) return -1;   // not expected: one store holds at most 8 items, one per kind
        long n = 0;
        foreach (var (item, count) in items) if (item.Key == (ulong)kind) n += count;
        return n;
    }

    /// <summary>All bodies of a side, read from the Pleroma roster.</summary>
    public long RosterTotal(Side side)
    {
        long n = 0;
        for (int lane = 0; lane < _setup.Lanes; lane++) n += _roster[(int)side * _setup.Lanes + lane].Total();
        return n;
    }

    /// <summary>Bodies of a side standing now, counted from the bodies (the roster must agree).</summary>
    public int Bodies(Side side) { int n = 0; foreach (var u in _units) if (u.Side == side) n++; return n; }

    /// <summary>
    /// FNV-1a over everything the future of the match depends on: the clock, status, both bases, the orders, the Pleroma rosters, and every body (id, side, lane, kind,
    /// hit points, position and facing as raw float bits, goal, cooldown, stillness, target, path cursor). Equal state, equal hash; any one body or base hit point differing
    /// changes it.
    /// </summary>
    public ulong StateHash()
    {
        var h = new Fnv();
        h.Add(_tick); h.Add((int)_status); h.Add(_seq[0]); h.Add(_seq[1]);
        if (!_probe.HasFlag(LaneProbe.HashIgnoresBase)) { h.Add(_baseHp[0]); h.Add(_baseHp[1]); }
        foreach (var o in _orders) h.Add((int)o);
        foreach (var store in _roster)
        {
            h.Add(store.Total());
            if (store.TryItems(out var items))
            {
                var sorted = items.OrderBy(x => x.Item.Key).ThenBy(x => x.Item.Arrangement).ThenBy(x => x.Item.Flags).ThenBy(x => x.Item.Time);
                foreach (var (item, count) in sorted) { h.Add((long)item.Key); h.Add((long)item.Arrangement); h.Add((long)item.Flags); h.Add(count); }
            }
            else h.Add(-1);
        }
        h.Add(_units.Count);
        for (int i = 0; i < _units.Count; i++)
        {
            var u = _units[i];
            h.Add(u.Id); h.Add(u.Seq); h.Add((int)u.Side); h.Add(u.Lane); h.Add(u.Kind);
            if (!_probe.HasFlag(LaneProbe.HashIgnoresHp)) h.Add(u.Hp);
            if (!(i == _units.Count - 1 && _probe.HasFlag(LaneProbe.HashIgnoresLastBody)))
            { h.Add(BitConverter.SingleToInt32Bits(u.P.X)); h.Add(BitConverter.SingleToInt32Bits(u.P.Y)); }
            h.Add(BitConverter.SingleToInt32Bits(u.Face.X)); h.Add(BitConverter.SingleToInt32Bits(u.Face.Y));
            h.Add(BitConverter.SingleToInt32Bits(u.Goal.X)); h.Add(BitConverter.SingleToInt32Bits(u.Goal.Y));
            h.Add(BitConverter.SingleToInt32Bits(u.Final.X)); h.Add(BitConverter.SingleToInt32Bits(u.Final.Y));
            h.Add(u.Cooldown); h.Add(u.Still); h.Add(u.Target); h.Add(u.PathIdx); h.Add(u.RetryAt);
            h.Add((u.NeedsPath ? 1 : 0) | (u.Engaged ? 2 : 0) | (u.ForceThink ? 4 : 0));
        }
        return h.Value;
    }

    // ---- test hooks (internal; the lane proof is a friend assembly): change ONE number of the state to see the hash move ----------------

    internal int BodyCountForTest => _units.Count;
    internal void PokeBodyHp(int index, int delta) => _units[index].Hp += delta;
    internal void PokeBodyX(int index, float delta) => _units[index].P += new Vector2(delta, 0f);
    internal void PokeBodyY(int index, float delta) => _units[index].P += new Vector2(0f, delta);
    internal void PokeBaseHp(Side side, long delta) => _baseHp[(int)side] += delta;

    /// <summary>
    /// The replay of the match so far: the setup, the seed, every play batch with the tick it was applied at, and the hash at each pause. It is plays + seed, not snapshots:
    /// a few hundred bytes for a whole match against hundreds of kilobytes (see <see cref="LaneReplayCodec"/>). Playing it back re-simulates.
    /// </summary>
    public LaneReplay Replay => new(1, _setup, _seed, _playLog.Select(e => new LanePlayBatch(e.Tick, e.Plays)).ToList(), _pauseLog.Select(p => new LanePause(p.Tick, p.Hash)).ToList());
}
