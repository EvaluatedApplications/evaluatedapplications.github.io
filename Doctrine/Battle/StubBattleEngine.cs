namespace Doctrine.Battle;

// STUB for seam (b), owned by tracer-owner. A fake battle in integer arithmetic: cohort blobs walk toward each other on a flat
// arena and trade damage in range. It exists so the pipeline, the replay and every contract test run headless today.
// The real section (Tracer navmesh, behaviours, engagement service) is written beside this file, in the folder Doctrine\Battle\, and
// must pass BattleContractTests and ReplayContractTests. This file STAYS as the reference for the shared mutants and the headless
// default (SECTIONS.md). Only tests and composition roots may name these types.

public sealed class StubBattleEngine : IBattleEngine
{
    static readonly ArenaInfo[] Catalogue =
    [
        new("open-field", "Open Field", 64, 40),
        new("chokepoint", "The Chokepoint", 48, 40),
        new("ridge", "Ridge Road", 80, 32),
    ];

    public IReadOnlyList<ArenaInfo> Arenas => Catalogue;

    public ValueTask<IBattleSession> StartAsync(BattleSetup setup, CancellationToken ct)
    {
        var arena = Catalogue.FirstOrDefault(a => a.Id == setup.ArenaId) ?? Catalogue[0];
        return ValueTask.FromResult<IBattleSession>(new StubBattleSession(setup, arena));
    }
}

public sealed class StubBattleSession : IBattleSession
{
    sealed class Blob
    {
        public int Id, Key, Slot, X, Y, Strength, Start, PowerPct, Speed, Range, TargetId = -1;
        public Side Side; public Trait Traits; public FieldOrderKind Order; public int Pending;
    }

    readonly BattleSetup _setup;
    readonly ArenaInfo _arena;
    readonly List<Blob> _blobs = new();
    readonly List<ReplayEvent> _events = new();

    public StubBattleSession(BattleSetup setup, ArenaInfo arena)
    {
        _setup = setup; _arena = arena;
        Add(setup.Attacker, 2 * BattleReplay.Quantum);
        Add(setup.Defender, (arena.Width - 2) * BattleReplay.Quantum);
    }

    void Add(BattleSide side, int x)
    {
        foreach (var c in side.Cohorts.Where(c => c.Count > 0))
        {
            var order = side.Orders.FirstOrDefault(o => o.Group == c.Slot);
            int speed = BattleReplay.Quantum / 2 * ((c.Traits & Trait.Swift) != 0 ? 2 : 1) * ((c.Traits & Trait.Heavy) != 0 ? 1 : 2) / 2;
            _blobs.Add(new Blob
            {
                Id = _blobs.Count, Key = c.Key, Slot = c.Slot, Side = side.Side, Traits = c.Traits, Strength = c.Count, Start = c.Count,
                PowerPct = c.PowerPct, Speed = Math.Max(2, speed),
                Range = ((c.Traits & Trait.Ranged) != 0 ? 6 : 2) * BattleReplay.Quantum,
                X = x, Y = (_arena.Height / 4) * (c.Slot + 1) * BattleReplay.Quantum,
                Order = order.Weight > 0 ? order.Kind : FieldOrderKind.Advance,
            });
        }
    }

    int StrengthOf(Side s) => _blobs.Where(b => b.Side == s).Sum(b => b.Strength);
    long PowerOf(Side s) => _blobs.Where(b => b.Side == s).Sum(b => UnitRules.Rating(b.Strength, b.PowerPct, b.Traits));
    IEnumerable<Blob> Alive(Side s) => _blobs.Where(b => b.Side == s && b.Strength > 0);

    public int Tick { get; private set; }

    public BattleStatus Status
    {
        get
        {
            // The attacker is Side.Player in a player-initiated battle, but the session only knows attacker/defender by setup.
            int a = StrengthOf(_setup.Attacker.Side), d = StrengthOf(_setup.Defender.Side);
            if (a == 0 && d == 0) return BattleStatus.Draw;
            if (d == 0) return BattleStatus.AttackerWon;
            if (a == 0) return BattleStatus.DefenderWon;
            return BattleStatus.Running;
        }
    }

    public ValueTask FieldOrdersAsync(CancellationToken ct)
    {
        foreach (var b in _blobs.Where(b => b.Strength > 0))
        {
            var foes = Alive(b.Side == Side.Player ? Side.Enemy : Side.Player).ToList();
            if (foes.Count == 0) { b.TargetId = -1; continue; }
            Blob? best = null; long bestScore = long.MaxValue;
            foreach (var f in foes)
            {
                long dx = f.X - b.X, dy = f.Y - b.Y, dist = dx * dx + dy * dy;
                long score = b.Order == FieldOrderKind.Hunt ? f.Strength * 1_000_000L + dist : dist;   // Hunt: the weakest first
                if (score < bestScore) { bestScore = score; best = f; }
            }
            b.TargetId = best!.Id;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask MoveAsync(CancellationToken ct)
    {
        foreach (var b in _blobs.Where(b => b.Strength > 0 && b.TargetId >= 0))
        {
            var t = _blobs[b.TargetId];
            int dir = Math.Sign(t.X - b.X), dy = Math.Sign(t.Y - b.Y);
            switch (b.Order)
            {
                case FieldOrderKind.Hold: case FieldOrderKind.Guard: break;
                case FieldOrderKind.FallBack:
                    b.X = Clamp(b.X - dir * b.Speed, _arena.Width * BattleReplay.Quantum); break;
                case FieldOrderKind.Flank:
                    b.Y = Clamp(b.Y + dy * b.Speed, _arena.Height * BattleReplay.Quantum);
                    b.X = Clamp(b.X + dir * (b.Speed / 2), _arena.Width * BattleReplay.Quantum); break;
                default:
                    if (Math.Abs(t.X - b.X) > b.Range / 2) b.X = Clamp(b.X + dir * b.Speed, _arena.Width * BattleReplay.Quantum);
                    b.Y = Clamp(b.Y + dy * Math.Min(b.Speed / 2, Math.Abs(t.Y - b.Y)), _arena.Height * BattleReplay.Quantum); break;
            }
        }
        return ValueTask.CompletedTask;
    }

    static int Clamp(int v, int max) => Math.Clamp(v, 0, max);

    public ValueTask EngageAsync(CancellationToken ct)
    {
        _events.Clear();
        foreach (var b in _blobs.Where(b => b.Strength > 0 && b.TargetId >= 0))
        {
            var t = _blobs[b.TargetId];
            long dx = t.X - b.X, dy = t.Y - b.Y;
            if (dx * dx + dy * dy > (long)b.Range * b.Range) continue;
            int dmg = Math.Max(1, b.Strength * b.PowerPct / 600);   // proportional to strength: the bigger force wins and survives
            if ((t.Traits & Trait.Shielded) != 0) dmg = Math.Max(1, dmg * 3 / 4);
            t.Pending += dmg;
            _events.Add(new(b.Id, t.Id, dmg));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyLossesAsync(CancellationToken ct)
    {
        foreach (var b in _blobs) { b.Strength = Math.Max(0, b.Strength - b.Pending); b.Pending = 0; }
        return ValueTask.CompletedTask;
    }

    public ReplayFrame Snapshot() => new(Tick,
        _blobs.Select(b => new ReplayBlob(b.Id, b.Side, b.Key, b.Slot, b.X, b.Y, b.Strength)).ToList(), Array.Empty<ReplayEvent>());

    public ReplayFrame Record()
    {
        Tick++;
        var f = new ReplayFrame(Tick,
            _blobs.Select(b => new ReplayBlob(b.Id, b.Side, b.Key, b.Slot, b.X, b.Y, b.Strength)).ToList(), _events.ToList());
        _events.Clear();
        return f;
    }

    public BattleResult Finish(bool tickLimitReached)
    {
        var outcome = Status;
        if (outcome == BattleStatus.Running)
        {
            // The limit counts POWER left (CONTRACT.md b), the same quantity the Tracer section counts: soldiers x power x damage x reach / taken.
            long a = PowerOf(_setup.Attacker.Side), d = PowerOf(_setup.Defender.Side);
            outcome = a > d ? BattleStatus.AttackerWon : d > a ? BattleStatus.DefenderWon : BattleStatus.Draw;
        }
        IReadOnlyList<CohortLoss> Losses(Side s) => _blobs.Where(b => b.Side == s && b.Start - b.Strength > 0)
            .Select(b => new CohortLoss(b.Key, b.Slot, b.Start - b.Strength)).ToList();
        return new(outcome, Tick, Losses(_setup.Attacker.Side), Losses(_setup.Defender.Side));
    }
}
