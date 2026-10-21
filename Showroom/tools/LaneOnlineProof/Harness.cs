using Doctrine;
using Doctrine.Lane;
using ShowroomLane;
using ShowroomPlenum;
using Tracer;
using Tracer.Integration;

static class H
{
    public static int Failures, Checks, BatchFails;
    public static string FirstRed = "";
    public static bool Quiet;

    public static void Check(bool ok, string what)
    {
        Checks++;
        if (!ok)
        {
            BatchFails++;
            if (BatchFails == 1) FirstRed = what;
            if (!Quiet) { Failures++; Console.WriteLine("  FAIL: " + what); }
        }
    }

    public static void Section(string s) { if (!Quiet) Console.WriteLine("== " + s); }
    public static void Note(string s) { if (!Quiet) Console.WriteLine("  " + s); }
}

sealed class TestClock : IPlenumClock
{
    public long NowMs { get; set; } = 5_000_000;
    public long UnixMs { get; set; } = 1_760_000_000_000;
    public void Advance(long ms) { NowMs += ms; UnixMs += ms; }
}

/// <summary>Wraps a relay so a proof can repeat, drop, hold back or reorder what a player sends. Everything else passes through.</summary>
sealed class GateRelay : IPlenumRelay
{
    public readonly LoopbackRelay Loop;
    public bool Dup;
    /// <summary>Topics for which this returns true are silently not sent.</summary>
    public Func<string, bool>? Mute;
    /// <summary>Topics for which this returns true are held back until the next reveal is sent (so a commitment can arrive after its reveal).</summary>
    public Func<string, bool>? Hold;
    readonly List<(string Topic, byte[] Payload, bool Retain, int Qos)> _held = [];
    public GateRelay(LoopbackRelay inner)
    {
        Loop = inner;
        inner.OnMessage += m => OnMessage?.Invoke(m);
        inner.OnDisconnect += r => OnDisconnect?.Invoke(r);
        inner.OnReconnected += () => OnReconnected?.Invoke();
    }
    public string ClientId => Loop.ClientId;
    public RelayState State => Loop.State;
    public event Action<RelayMessage>? OnMessage;
    public event Action<string>? OnDisconnect;
    public event Action? OnReconnected;
    public Task ConnectAsync(RelayWill? will = null, CancellationToken ct = default) => Loop.ConnectAsync(will, ct);
    public Task SubscribeAsync(string filter) => Loop.SubscribeAsync(filter);
    public Task UnsubscribeAsync(string filter) => Loop.UnsubscribeAsync(filter);
    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, bool retain = false, int qos = 0)
    {
        if (Mute?.Invoke(topic) == true && payload.Length > 0) return;
        if (Hold?.Invoke(topic) == true && payload.Length > 0) { _held.Add((topic, payload.ToArray(), retain, qos)); return; }
        await Loop.PublishAsync(topic, payload, retain, qos);
        if (Dup) await Loop.PublishAsync(topic, payload, retain, qos);
        if (topic.Contains("/reveal/") && _held.Count > 0)
        {
            foreach (var h in _held.ToArray()) await Loop.PublishAsync(h.Topic, h.Payload, h.Retain, h.Qos);
            _held.Clear();
        }
    }
    public Task DisconnectAsync() => Loop.DisconnectAsync();
    public ValueTask DisposeAsync() => Loop.DisposeAsync();
}

sealed class GateFactory(LoopbackBroker broker) : IPlenumRelayFactory
{
    public readonly List<GateRelay> Made = [];
    public Action<GateRelay>? Tune;
    public IPlenumRelay Create(string clientId) { var r = new GateRelay(broker.NewClient(clientId)); Tune?.Invoke(r); Made.Add(r); return r; }
    /// <summary>The relay a player's session plays on (the first made with the session's own id prefix).</summary>
    public GateRelay Session => Made.First(r => r.ClientId.StartsWith("ll") && !r.ClientId.EndsWith('x'));
}

/// <summary>A passive observer on the in-memory broker: sees every message every player publishes, in order, with the clock reading at the moment it was sent.</summary>
sealed class Monitor
{
    public readonly List<(int Index, string Topic, byte[] Payload, long Unix)> Log = [];
    public int Clears;
    public Monitor(LoopbackBroker b, TestClock clock)
    {
        var relay = b.NewClient("monitor");
        relay.OnMessage += m =>
        {
            if (m.Payload.Length == 0) { Clears++; return; }
            Log.Add((Log.Count, m.Topic, m.Payload, clock.UnixMs));
        };
        relay.ConnectAsync().GetAwaiter().GetResult();
        relay.SubscribeAsync("plenum/v1/#").GetAwaiter().GetResult();
    }

    public IEnumerable<string> Topics => Log.Select(l => l.Topic).Distinct();
    public IEnumerable<string> TopicsOutsideRoot => Topics.Where(t => !t.StartsWith(LaneOnlineProtocol.Root, StringComparison.Ordinal));

    /// <summary>For every pause (of one game): the first reveal message comes after BOTH commitments (by position on the wire).</summary>
    public (bool Ok, string Why) RevealsAfterCommits(string code)
    {
        string root = LaneOnlineProtocol.RoomRoot + code + "/";
        var firstCommit = new Dictionary<(string, string, string), int>(); var firstReveal = new Dictionary<(string, string), int>();
        foreach (var l in Log)
        {
            if (!l.Topic.StartsWith(root, StringComparison.Ordinal)) continue;
            var parts = l.Topic[root.Length..].Split('/');
            if (parts.Length == 4 && parts[1] == "commit") firstCommit.TryAdd((parts[0], parts[2], parts[3]), l.Index);
            if (parts.Length == 4 && parts[1] == "reveal") firstReveal.TryAdd((parts[0], parts[2]), l.Index);
        }
        foreach (var (key, at) in firstReveal)
        {
            if (!firstCommit.TryGetValue((key.Item1, key.Item2, "host"), out int ch) || !firstCommit.TryGetValue((key.Item1, key.Item2, "guest"), out int cg)) return (false, $"{key.Item1} pause {key.Item2}: a reveal with a commitment missing");
            if (at < Math.Max(ch, cg)) return (false, $"{key.Item1} pause {key.Item2}: a reveal (message {at}) went out before both commitments (messages {ch} and {cg})");
        }
        return (true, "");
    }
}

static class Rig
{
    static readonly string Scratch = Path.Combine(Path.GetTempPath(), "lane-online-proof-" + Guid.NewGuid().ToString("N"));
    public static TracerContext Ctx() => new(new PathfindingWorld(tunerStatePath: Scratch));
    /// <summary>The setup of every online proof: the page's three lanes, with a hard tick cap of eight phases so a match that nobody wins still ends.</summary>
    public static LaneSetup Setup => new() { TickCap = 8 * LaneRules.PhaseTicks };
    static int _codes;
    public static string NextCode() => "ZT" + (1000 + Interlocked.Increment(ref _codes)).ToString();
}

/// <summary>A seat that plays a fixed plan for each pause (the offline replay of what two online players did).</summary>
sealed class ScriptedSeat(Dictionary<int, LanePlan> plans) : ILaneSeat
{
    public bool IsHuman => false;
    public ValueTask<LanePlan> PlanAsync(LaneSeatView view, CancellationToken ct) => ValueTask.FromResult(plans.TryGetValue(view.Phase, out var p) ? p : LanePlan.Empty);
}

/// <summary>Two in-process players on one in-memory broker with one hand-driven clock: the host (A) and the guest (B).</summary>
sealed class Pair
{
    public readonly TestClock Clock = new();
    public readonly LoopbackBroker Broker = new();
    public readonly GateFactory FactoryA, FactoryB;
    public LaneOnline A = null!, B = null!;
    public Monitor Mon = null!;
    public string Code = "";
    public bool AutoA, AutoB, LazyA, LazyB;
    /// <summary>What each side committed at each pause (for the offline replay).</summary>
    public readonly Dictionary<Side, Dictionary<int, LanePlan>> Plans = new() { [Side.Player] = new(), [Side.Enemy] = new() };
    readonly LaneOnlineProbe _pa, _pb;
    readonly Action<LaneOnlineOptions>? _tune;
    /// <summary>An option change for the guest only (set before StartAsync / CreateAndJoinAsync).</summary>
    public Action<LaneOnlineOptions>? TuneB;

    internal Pair(LaneOnlineProbe pa = LaneOnlineProbe.None, LaneOnlineProbe pb = LaneOnlineProbe.None, Action<LaneOnlineOptions>? tune = null)
    {
        FactoryA = new GateFactory(Broker); FactoryB = new GateFactory(Broker);
        _pa = pa; _pb = pb; _tune = tune;
    }

    public LaneOnlineOptions Opts(GateFactory f, LaneOnlineProbe probe)
    {
        var o = new LaneOnlineOptions
        {
            Relays = f, Clock = Clock, Tracer = () => Rig.Ctx(), Setup = Rig.Setup, JoinWaitMs = 300, SeatWaitMs = 3000, CreateProbeMs = 15, PresenceClearDelayMs = 0,
            RunDtCap = 31, TicksPerAdvance = 400, SeedSource = () => 7, CodeSource = () => Code, Probe = probe,
        };
        _tune?.Invoke(o);
        if (f == FactoryB) TuneB?.Invoke(o);
        return o;
    }

    /// <summary>Creates the room, lets the guest join (the host answers from a loop, as its ticker would), and returns the guest's refusal if any.</summary>
    public async Task<string?> CreateAndJoinAsync(string hostName = "Lord Ada", string guestName = "Lord Bo")
    {
        Code = Rig.NextCode();
        Mon = new Monitor(Broker, Clock);
        A = new LaneOnline(Opts(FactoryA, _pa)); B = new LaneOnline(Opts(FactoryB, _pb));
        await A.CreateRoomAsync(hostName);
        var j = B.JoinAsync(" " + Code.ToLowerInvariant() + " ", guestName);
        while (!j.IsCompleted) { await A.AdvanceAsync(); await Task.Delay(5); }
        return await j;
    }

    public async Task Step(int ms, bool a = true, bool b = true)
    {
        Clock.Advance(ms);
        if (a) await A.AdvanceAsync();
        if (b) await B.AdvanceAsync();
        if (AutoA && a) await Autopilot(A);
        if (AutoB && b) await Autopilot(B);
    }

    public async Task<bool> Until(Func<bool> cond, int stepMs, int max)
    {
        for (int i = 0; i < max; i++) { if (cond()) return true; await Step(stepMs); }
        return cond();
    }

    /// <summary>Joins and runs the clock until both are in the battle (the first pause open).</summary>
    public async Task<string?> StartAsync(string hostName = "Lord Ada", string guestName = "Lord Bo")
    {
        var err = await CreateAndJoinAsync(hostName, guestName);
        if (err is not null) return err;
        await Until(() => A.Phase == OnlinePhase.Playing && B.Phase == OnlinePhase.Playing, 500, 80);
        await Step(0);
        return null;
    }

    /// <summary>Stages a few packs and an order (the same way for a given pause and side) and remembers the plan; true when it is staged.</summary>
    public bool StageOnly(LaneOnline s)
    {
        if (s.Phase != OnlinePhase.Playing || !s.PauseOpen || s.MyCommitted || s.Match is not { } m || m.Human is not { } h) return false;
        int n = m.Phase, side = (int)m.LocalSide, lanes = m.Battle.Setup.Lanes;
        if (s == A ? LazyA : LazyB) { Plans[m.LocalSide][n] = LanePlan.Empty; return true; }
        for (int k = 0; k < 8; k++) h.Buy((n + k + side) % lanes, (n + side + k) % 8, out _);
        if (n % 2 == 1) h.Order((n + side) % lanes, FieldOrderKind.Hold, out _);
        Plans[m.LocalSide][n] = h.CurrentPlan();
        return true;
    }

    /// <summary>Stages (see StageOnly) and presses Ready.</summary>
    public async Task Autopilot(LaneOnline s)
    {
        if (StageOnly(s)) await s.ReadyAsync();
    }

    /// <summary>Plays both players to the end of the match (each presses Ready at once at every pause).</summary>
    public async Task PlayOutAsync(int maxSteps = 400)
    {
        AutoA = AutoB = true;
        for (int i = 0; i < maxSteps && !(A.Phase == OnlinePhase.Ended && B.Phase == OnlinePhase.Ended); i++)
        {
            bool bothRun = A.Match is { Stage: MatchStage.Run } && B.Match is { Stage: MatchStage.Run };
            await Step(bothRun ? 30_000 : 250);
        }
        AutoA = AutoB = false;
    }

    /// <summary>Runs the clock with both players on Ready-at-once until <paramref name="stop"/> (small steps in a pause, a whole run in one step).</summary>
    public async Task PlayUntil(Func<bool> stop, int maxSteps = 400)
    {
        AutoA = AutoB = true;
        for (int i = 0; i < maxSteps && !stop(); i++)
        {
            bool bothRun = A.Match is { Stage: MatchStage.Run } && B.Match is { Stage: MatchStage.Run };
            await Step(bothRun ? 30_000 : 250);
        }
    }

    public GateRelay RelayOf(bool host) => host ? FactoryA.Session : FactoryB.Session;

    public async Task DisposeAsync() { try { await A.LeaveAsync(); } catch { } try { await B.LeaveAsync(); } catch { } }
}
