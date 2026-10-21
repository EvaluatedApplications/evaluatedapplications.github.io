using Doctrine;
using Doctrine.Lane;
using Tracer.Integration;

namespace ShowroomLane;

public enum MatchStage
{
    /// <summary>A pause: the board stands still, the clock counts down, plays may be staged. Tick 0 is one.</summary>
    Pause,
    /// <summary>The battle runs at ten ticks a second of game time (times the speed).</summary>
    Run,
    /// <summary>The match is over (see <see cref="LaneMatch.Outcome"/>).</summary>
    Over,
}

public sealed record LaneMatchOptions
{
    public int Seed { get; init; } = 1;
    public LaneSetup Setup { get; init; } = DefaultSetup;
    /// <summary>Null = the human seat (a <see cref="ManualSeat"/> the page drives).</summary>
    public ILaneSeat? Player { get; init; }
    /// <summary>Null = a <see cref="LaneBot"/>.</summary>
    public ILaneSeat? Enemy { get; init; }
    /// <summary>Most ticks one <see cref="LaneMatch.AdvanceAsync"/> call may run. A slow device then plays slower than the clock instead of freezing the page (see <see cref="LaneMatch.Behind"/>).</summary>
    public int MaxTicksPerAdvance { get; init; } = 6;
    internal LaneMatchProbe Probe { get; init; }

    /// <summary>The setup of the playable page and of the desktop hash line: three lanes, the library's defaults for the rest.</summary>
    public static LaneSetup DefaultSetup { get; } = new();
}

/// <summary>What one <see cref="LaneMatch.AdvanceAsync"/> did.</summary>
public readonly record struct LaneAdvance(int Ticks, bool StageChanged);

/// <summary>
/// The playable lane match: ONE service that drives <see cref="LaneBattle"/> in real time and owns nothing of a screen. The caller hands in elapsed seconds
/// (<see cref="AdvanceAsync"/>); the match does the rest.
/// <para>
/// THE LOOP. RUN: the sim advances ten ticks per second of game time, times <see cref="Speed"/> (1, 2 or 4), until the next pause, one tick per StepAsync so a renderer can
/// draw the picture BETWEEN the last two ticks (<see cref="Bodies"/>, <see cref="Alpha"/>). PAUSE: <see cref="PauseSeconds"/> to choose (real seconds, speed does not touch
/// them), ended early by <see cref="Ready"/>. When the pause ends both seats' plans are priced, merged and applied in ONE <see cref="LaneBattle.ApplyPlays"/> call at the pause tick,
/// so the two sides' plays land together, then the next run starts. A pause with only bots at the table (no human seat) does not wait.
/// </para>
/// <para>Same seed, same setup and same plays give the same match: the match adds nothing of its own (the bot draws from the seed, time never enters the battle).</para>
/// </summary>
public sealed class LaneMatch
{
    readonly LaneBattle _b;
    readonly LaneMatchOptions _o;
    readonly LaneMatchProbe _probe;
    readonly ILaneSeat[] _seats = new ILaneSeat[2];
    readonly LaneEconomy[] _econ = new LaneEconomy[2];
    readonly List<(int Tick, ulong Hash)> _hashes = new();
    readonly List<LaneBodyDraw> _draw = new();
    double _debt;
    bool _ready;

    public const double PauseSeconds = LaneRules.PhaseSeconds;
    public const int MaxSpeed = 4;

    public MatchStage Stage { get; private set; } = MatchStage.Pause;
    public LaneBattle Battle => _b;
    public int Seed => _o.Seed;
    public LaneSnapshot Prev { get; private set; }
    public LaneSnapshot Curr { get; private set; }
    /// <summary>How far the clock is from <see cref="Prev"/> to <see cref="Curr"/> (0 to 1): draw the bodies that far between the two.</summary>
    public double Alpha { get; private set; } = 1;
    /// <summary>The run speed, 1, 2 or 4. It changes how fast the battle runs, never how long a pause lasts.</summary>
    public int Speed { get; private set; } = 1;
    /// <summary>Real seconds left of the pause (only meaningful in a pause).</summary>
    public double PauseLeft { get; private set; } = PauseSeconds;
    /// <summary>True when the last advance could not run all the ticks the clock asked for (a slow device): the battle is running slower than its speed.</summary>
    public bool Behind { get; private set; }
    public LaneStatus Status => _b.Status;
    /// <summary>The state hash at every pause passed (tick 0 first) and the tick the match ended on, in order.</summary>
    public IReadOnlyList<(int Tick, ulong Hash)> PauseHashes => _hashes;
    /// <summary>Counted at every pause that ended: how many plays the two seats applied (for the page's line "your plays at work").</summary>
    public IReadOnlyList<LanePlayResult> LastResults { get; private set; } = Array.Empty<LanePlayResult>();
    /// <summary>What each seat's plan came to at the last pause end (after pricing).</summary>
    public LanePlan[] LastPlans { get; } = [LanePlan.Empty, LanePlan.Empty];
    /// <summary>The human seat, or null when a seat was passed in.</summary>
    public ManualSeat? Human { get; }
    public int Phase => _b.Phase;
    public int Tick => _b.Tick;
    public bool AnyHuman => _seats[0].IsHuman || _seats[1].IsHuman;
    public LaneEconomy Economy(Side side) => _econ[(int)side];

    LaneMatch(LaneBattle b, LaneMatchOptions o)
    {
        _b = b; _o = o; _probe = o.Probe;
        _econ[0] = new LaneEconomy(_probe); _econ[1] = new LaneEconomy(_probe);
        var player = o.Player; var enemy = o.Enemy ?? new LaneBot(_probe);
        if (player is null) { Human = new ManualSeat(_econ[0], b.Setup.Lanes, _probe) { Open = () => Stage == MatchStage.Pause }; player = Human; }
        _seats[0] = player; _seats[1] = enemy;
        Prev = Curr = b.Snapshot();
        EnterPause(b.StateHash());
    }

    public static async Task<LaneMatch> CreateAsync(LaneMatchOptions o, TracerContext? tracer = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(o);
        var b = await LaneBattle.CreateAsync(o.Setup, o.Seed, tracer, ct);
        return new LaneMatch(b, o);
    }

    /// <summary>Sets the run speed (1, 2 or 4). Refused (false) for any other number.</summary>
    public bool SetSpeed(int speed)
    {
        if (speed is not (1 or 2 or MaxSpeed)) return false;
        Speed = speed;
        return true;
    }

    /// <summary>Ends the pause early (the Ready button). False outside a pause.</summary>
    public bool Ready()
    {
        if (Stage != MatchStage.Pause || _probe.HasFlag(LaneMatchProbe.ReadyIgnored)) return false;
        _ready = true;
        return true;
    }

    /// <summary>The result once the match is over, otherwise null.</summary>
    public LaneStatus? Outcome => Stage == MatchStage.Over ? _b.Status : null;

    void EnterPause(ulong hash)
    {
        Stage = MatchStage.Pause; PauseLeft = PauseSeconds; _ready = false; _debt = 0; Alpha = 1;
        _hashes.Add((_b.Tick, hash));
        _econ[0].Credit(); _econ[1].Credit();
        Human?.Clear();
    }

    /// <summary>
    /// Moves the match on by <paramref name="dt"/> real seconds. In a run it steps whole ticks (one StepAsync each) and keeps the part of a tick left over; at the pause it
    /// counts the clock down and, when the pause ends (clock or Ready, or at once with no human at the table), applies both seats' plays and starts the next run.
    /// </summary>
    public async ValueTask<LaneAdvance> AdvanceAsync(double dt, CancellationToken ct = default)
    {
        if (dt < 0 || double.IsNaN(dt)) dt = 0;
        var before = Stage; int ran = 0;
        switch (Stage)
        {
            case MatchStage.Run:
            {
                double rate = LaneRules.TicksPerSecond + (_probe.HasFlag(LaneMatchProbe.RunTooFast) ? 1 : 0);
                _debt += dt * rate * Speed;
                int want = (int)_debt, n = Math.Min(want, _o.MaxTicksPerAdvance);
                Behind = want > n;
                if (Behind) _debt = n + Math.Min(_debt - want, 1.0);   // a slow device does not carry a backlog: it plays slower
                for (int i = 0; i < n && Stage == MatchStage.Run; i++)
                {
                    var r = await _b.StepAsync(1, ct);
                    ran += r.TicksRun; _debt -= r.TicksRun;
                    Prev = Curr; Curr = _b.Snapshot();
                    if (_b.Status != LaneStatus.Running) { Finish(r.Hash); }
                    else if (r.AtPause) EnterPause(r.Hash);
                }
                if (Stage == MatchStage.Run) Alpha = Math.Clamp(_debt, 0.0, 1.0);
                break;
            }
            case MatchStage.Pause:
            {
                double scale = _probe.HasFlag(LaneMatchProbe.SpeedScalesPause) ? Speed : 1.0;
                if (!_probe.HasFlag(LaneMatchProbe.PauseClockIgnored)) PauseLeft -= dt * scale;
                if (!AnyHuman || _ready || (PauseLeft <= 0 && !_probe.HasFlag(LaneMatchProbe.PauseClockIgnored))) await EndPauseAsync(ct);
                break;
            }
        }
        return new LaneAdvance(ran, Stage != before);
    }

    void Finish(ulong hash)
    {
        Stage = MatchStage.Over; Alpha = 1;
        _hashes.Add((_b.Tick, hash));
    }

    /// <summary>Prices both plans, applies them together at this tick and starts the run.</summary>
    async ValueTask EndPauseAsync(CancellationToken ct)
    {
        var plays = new List<LanePlay>[2] { new(), new() };
        for (int s = 0; s < 2; s++)
        {
            var side = (Side)s;
            var view = ViewFor(side);
            var plan = await _seats[s].PlanAsync(view, ct);
            var packs = new List<PackBuy>(); var orders = new List<LaneOrderChoice>();
            foreach (var o in plan.Orders)
            {
                if ((uint)o.Lane >= (uint)_b.Setup.Lanes) continue;
                orders.Add(o); plays[s].Add(new OrderPlay(side, o.Lane, o.Order));
            }
            foreach (var p in plan.Packs)
            {
                if ((uint)p.Lane >= (uint)_b.Setup.Lanes || !_econ[s].TrySpend(p.Kind)) continue;   // over the purse: refused, nothing changes
                packs.Add(p); plays[s].Add(new SpawnPlay(side, p.Lane, p.Kind, LaneEconomy.PackSize));
            }
            LastPlans[s] = new LanePlan(packs, orders);
        }
        if (_probe.HasFlag(LaneMatchProbe.PlaysInTwoBatches))
        {
            var a = _b.ApplyPlays(plays[0]);
            await _b.StepAsync(1, ct);
            var c = _b.ApplyPlays(plays[1]);   // a tick later, in the middle of a phase: the library refuses (the mutant is caught either way)
            LastResults = a.Concat(c).ToArray();
        }
        else LastResults = _b.ApplyPlays([.. plays[0], .. plays[1]]).ToArray();
        Human?.Clear();
        Stage = MatchStage.Run; _debt = 0; Alpha = 1; _ready = false;
        Prev = Curr = _b.Snapshot();
    }

    LaneSeatView ViewFor(Side side)
    {
        var orders = new FieldOrderKind[_b.Setup.Lanes];
        for (int l = 0; l < orders.Length; l++) orders[l] = _b.OrderOf(side, l);
        return new LaneSeatView(side, _o.Seed, _b.Phase, _b.Tick, _b.Snapshot(), orders, _econ[(int)side].Balance, _b.Bodies(side), _b.Setup.MaxUnitsPerSide, _b.Setup.Lanes);
    }

    /// <summary>
    /// Plays a whole match without a screen: end each pause, run the phase, until the match is over or <paramref name="maxPhases"/> phases are done. The same calls a real-time
    /// run makes, a phase at a time (the proof checks that the two give the same hashes). For the desktop hash line and the proofs.
    /// </summary>
    public async Task PlayToEndAsync(int maxPhases = int.MaxValue, CancellationToken ct = default)
    {
        for (int ph = 0; ph < maxPhases && Stage != MatchStage.Over; ph++)
        {
            if (Stage == MatchStage.Pause) await EndPauseAsync(ct);
            var r = await _b.StepAsync(int.MaxValue, ct);
            Prev = Curr; Curr = _b.Snapshot();
            if (_b.Status != LaneStatus.Running) Finish(r.Hash); else if (r.AtPause) EnterPause(r.Hash);
        }
    }

    /// <summary>The bodies to draw now: between <see cref="Prev"/> and <see cref="Curr"/> by <see cref="Alpha"/> (a list reused by the next call).</summary>
    public IReadOnlyList<LaneBodyDraw> Bodies()
    {
        LaneInterp.Between(Prev, Curr, _probe.HasFlag(LaneMatchProbe.InterpolationReversed) ? 1.0 - Alpha : Alpha, _draw);
        return _draw;
    }

    /// <summary>The same hash a browser shows, in short form (the first eight hex digits of the 64-bit hash).</summary>
    public static string ShortHash(ulong h) => h.ToString("x16")[..8];
}
